using System;
using System.Collections.Generic;
using System.Linq;
using Codice.Client.Common.GameUI;
using JetBrains.Annotations;
using RealtimeCSG.Components;
using Unity.FPS.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

struct CompositeLightmapData {
  
}

public static class Shaders {
  private const string lightmap = "Custom/CompositeLightmap";
  public static Shader Lightmap => Shader.Find(lightmap);

  private const string vertex = "Custom/MyVertexLit";
  public static Shader Vertex => Shader.Find(vertex);

  private const string basic = "Custom/MyBasic";
  public static Shader Basic => Shader.Find(basic);
}


public class GfxManager : MonoBehaviour {
  [SerializeField] private LightmapDb lightmapDb;
  [SerializeField] private TrackedLightSet lights;
  [SerializeField] private MainSettings settings;
  
  public void Start() {

    InitCompositeLightmaps();

    RenderSettings.ambientLight = settings.GameModeAmbientLightColor;
    RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
    
  }

  private void ConvertToLightmapMaterial(MeshRenderer meshR) {
    if (meshR.material.name.ToLower().Contains("skybox")) return;
    else if (meshR.material.shader.name.ToLower().Contains("unlit")) return;

    var texture = meshR.material.mainTexture;
    Shader lmShader = Shaders.Lightmap;

    meshR.material.shader = lmShader;
    meshR.material.SetTexture("_MainTex", texture);
  }

  void InitCompositeLightmaps() {
    if (lightmapDb.lightmapGroups == null || lightmapDb.lightmapGroups.Length == 0) {
      throw new Exception("Lightmap DB has no data!");
    }

    // read lightmaps from disk
    foreach (var group in lightmapDb.lightmapGroups) {

      var lms = Resources.LoadAll<Texture2D>($"Textures/lightmaps/{group.roomModelId}");

      var list = new List<Texture2D>();
      foreach (var l in lms) {
        if (l.name.StartsWith($"{group.Light.GetInstanceID()}")) {
          list.Add(l);
        }
      }
      group.lightmaps = list.ToArray();

    }

    var lightsByMeshRenderer = GetLightsByMeshRenderer(lightmapDb.lightmapGroups);

    var sampleLightmap = lightsByMeshRenderer.First().Value.Lightmaps.First();
    var lightmapRes = sampleLightmap.width;
    var lightmapFormat = sampleLightmap.format;
    
    foreach (var (meshR, lightdata) in lightsByMeshRenderer) {
      this.ConvertToLightmapMaterial(meshR);

      var textureArray = new Texture2DArray(lightmapRes, lightmapRes, lightdata.Lightmaps.Length, lightmapFormat, true, false); // true for mipChain
      for (int i = 0; i < lightdata.Lightmaps.Length; i++) {
          Graphics.CopyTexture(lightdata.Lightmaps[i], 0, textureArray, i);
      }
      textureArray.Apply(false, true);
      var lightColorArray = lightdata.Lights.Select(l => l.BakedLight.color).ToArray();

      meshR.material.SetTexture("_LightMaps", textureArray);
      meshR.material.SetInt("_LightmapCount", lightdata.Lightmaps.Length);
      meshR.material.SetVectorArray("_LightmapScaleOffsets", lightdata.LightmapScaleOffset);
      meshR.material.SetColorArray("_LightColors", lightdata.Lights.Select(l => l.Color * l.Intensity).ToArray());

      lightdata.Lights.ToList().ForEach((light) => {
        light.OnDirty += () => {
          int index = lightdata.Lights.ToList().IndexOf(light);
          lightColorArray[index] = light.Lit ? light.BakedLight.color * light.Intensity : Color.black;
          meshR.material.SetColorArray("_LightColors", lightColorArray);
        };
      });
    }
  }

  public class RestructuredLm {
    public LightTracked[] Lights;
    public Texture2D[] Lightmaps;
    public Vector4[] LightmapScaleOffset;
  }

  public static Dictionary<MeshRenderer, RestructuredLm> GetLightsByMeshRenderer(LightmapGroup[] lightmapGroups) {

    // foreach (var group in lightmapGroups) {
    //   Debug.Log(group.lightmaps.Length);
    // }

    return lightmapGroups
      // First, flatten the structure to get all combinations of MeshRenderer and Light
      .SelectMany(group =>
        group.meshLightmapData
          .Where(meshData => meshData.lightmapIndex != -1)
          .Select(meshData => new {
            MeshRenderer = meshData.owner,
            group.Light,
            lightmap = group.lightmaps[meshData.lightmapIndex],
            // lightmapScaleOffset = group.meshLightmapData[0].
            meshData.lightmapScaleOffset
          })
      )
      // Group by MeshRenderer
      .GroupBy(item => item.MeshRenderer)
      // Convert to dictionary where values are arrays of associated lights
      .ToDictionary(
        group => group.Key,
        group => new RestructuredLm() {
          Lights = group.Select(item => item.Light).ToArray(),
          Lightmaps = group.Select(item => item.lightmap).ToArray(),
          // LightmapScaleOffset = group.First().lightmapScaleOffset
          LightmapScaleOffset = group.Select(item => item.lightmapScaleOffset).ToArray(),
        }
      )
    ;
  }

  [MenuItem("Level/Bake All Lights")]
  public static void BakeAllLights() {
    // var lightsToBake = LightMapBaker.DetectDirtyLights();
    LightMapBaker.MultiBake();
  }

  [MenuItem("Level/Bake Selected Rooms")]
  public static void BakeSelectedRooms() {
    var selObjs = Selection.gameObjects;

    var lightsToBake = selObjs
      .Where(obj => {
        return obj.CompareTag("LEVEL_MODELS") && obj.GetComponent<CSGModel>() != null;
      })
      .SelectMany(models => {
        return models.GetComponentsInChildren<LightTracked>();
      })
      .ToArray()
    ;

    LightMapBaker.MultiBake(lightsToBake);
  }


  [MenuItem("Level/Assign Props")]
  public static void AssignProps() {
    // var roomModels = GameObject.FindGameObjectsWithTag("LEVEL_MODELS");
    var roomModels = GameObject.FindObjectsOfType<CSGModel>().Where(model => model.CompareTag("LEVEL_MODELS"));

    Transform roomPropFolder = null;
    var roomModelPropFolders = new Dictionary<string, Transform>();

    foreach (var csgModel in roomModels) {

      // create a child called 'Props' if one doesn't exist
      roomPropFolder = csgModel.transform.Find("Props");
      if (roomPropFolder == null) {
        roomPropFolder = new GameObject("Props").transform;
        roomPropFolder.SetParent(csgModel.transform);
      }
      roomPropFolder.transform.localPosition = Vector3.zero;
      roomModelPropFolders.Add(csgModel.GetComponentInParent<CSGModel>().name, roomPropFolder);
    }

    // var levelMask = 1 << LayerMask.NameToLayer("Level");
    var levelMask = LayerMask.GetMask("Level");
    var props = GameObject.FindGameObjectsWithTag("prop");

    foreach (var prop in props) {
      var propCol = prop.GetComponentInChildren<Collider>();
      var rayOrigin = propCol == null ? prop.transform.position : propCol.bounds.center;

      var dirs = new Vector3[] { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };

      var closestCollider = dirs
        .Select(dir => {
          var ray = new Ray(prop.transform.position, dir * 16);
          Debug.DrawRay(ray.origin, ray.direction, UnityEngine.Random.ColorHSV(), 8f);
          if (Physics.Raycast(ray, out RaycastHit hit, 16, levelMask)) {
            return hit.collider;
          }
          return null;
        })
        .FirstOrDefault(res => res != null) ?? null
      ;

      if (closestCollider != null) {
        var modelComponent = closestCollider.transform.GetComponentInParent<CSGModel>();

        if (modelComponent == null) {
          Debug.Log("bad collision", closestCollider.gameObject);
        }
        prop.transform.SetParent(roomModelPropFolders[modelComponent.name]);

        var propComponent = prop.GetComponent<AbstractRoomProp>();
        if (propComponent != null) {
          propComponent.abstractRoom = modelComponent.GetComponentInChildren<AbstractRoom>();
          PrefabUtility.RecordPrefabInstancePropertyModifications(propComponent);
        }
      }
    }
    EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
  }
}


