using System;
using System.Collections.Generic;
using System.Linq;
using R3;
using RealtimeCSG.Components;
using Unity.FPS.Game;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using System.IO;
using RealtimeCSG;
using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;




[Serializable]
public class LightmapGroup {

  public LightTracked Light;
  // determine if a light needs to be rebaked:
  // -> did the light move in the editor?
  // -> did the light color change in the editor?
  // -> did the light lit status change?
  // -> did any of the csg models depending on it change? (maybe later)
  public Vector3 lightPosition;
  public Color lightColor;
  public bool lightLit;
  public int roomModelId;
  public MeshLightmapData[] meshLightmapData;
  public Texture2D[] lightmaps;
}

[Serializable]
public struct MeshLightmapData {
  public MeshRenderer owner;
  public int lightmapIndex;
  public Vector4 lightmapScaleOffset;
  public MeshFilter test;
}

public static class LightMapBaker {

  internal static UnityAction<List<LightmapGroup>> OnBakeCompleted;


  private static void FixStaticProps(IEnumerable<(GameObject, MeshRenderer)> props) {
    foreach (var (go, meshRenderer) in props) {
      go.isStatic = false;
      // meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

      var name = go.name;
      var newGo = GameObject.Instantiate(go, go.transform.parent);
      GameObject.DestroyImmediate(go);
      newGo.name = name;
    }
  }

  // private static void FixVertexLights() {
  //   foreach (var lightGo in GameObject.FindGameObjectsWithTag("baked-vertex-light")) {
  //     var parentLight = lightGo.GetComponent<Light>();
  //     foreach (var child in lightGo.GetComponentsInChildren<Light>()) {
  //       child.intensity = parentLight.intensity;
  //       child.color = parentLight.color;
  //       child.range = parentLight.range;
  //     }
  //   }
  // }

  private static void ClearLightmaps() {
    var folders = AssetDatabase.GetSubFolders("Assets/Resources/Textures/lightmaps");

    foreach (var folder in folders) {
      AssetDatabase.DeleteAsset(folder);
    }
  }
  private static void ClearLightmaps(int[] modelId) {
    var folders = AssetDatabase.GetSubFolders("Assets/Resources/Textures/lightmaps");

    foreach (var folder in folders) {
      if ($"{modelId}".Equals(folder))
        AssetDatabase.DeleteAsset(folder);
    }
  }

  private static bool isLightmapsEnabled(GameObject go) {
    var staticFlags = GameObjectUtility.GetStaticEditorFlags(go);
    var csgModel = go.GetComponent<CSGModel>();

    if (csgModel == null)
      return false;

    // check if contributing to GI
    var usesLightmaps = csgModel.ReceiveGI == ReceiveGI.Lightmaps;
    var contributesGi = (staticFlags & StaticEditorFlags.ContributeGI) == StaticEditorFlags.ContributeGI;

    return usesLightmaps && contributesGi;
  }

  private static void ToggleContributeGi(GameObject go, bool toggle) {
    var staticFlags = GameObjectUtility.GetStaticEditorFlags(go);
    var newStaticFlags = toggle ? staticFlags | StaticEditorFlags.ContributeGI : staticFlags & ~StaticEditorFlags.ContributeGI;
    GameObjectUtility.SetStaticEditorFlags(go, newStaticFlags);
  }

  internal static void MultiBake(LightTracked[] selLights = null) {
    Debug.Log("Starting bake jobs...");
    if (selLights == null)
      selLights = new LightTracked[] {};

    // ClearLightmaps();
    // FixVertexLights();

    // never bake non level meshes
    foreach (var mesh in GameObject.FindObjectsOfType<MeshRenderer>()) {
      if (mesh.CompareTag("LEVEL_MODELS")) continue;
      mesh.scaleInLightmap = 0;
    }

    // todo: to be sure, scan for all models in the scene which have bakeable lightmaps and disable them temporarily?
    // ie. allBakableRenderers array
    var allLevelModels = GameObject.FindGameObjectsWithTag("LEVEL_MODELS")
      .Where(go => {
        var pass = isLightmapsEnabled(go);
        return pass;
      })
      .Select(model => new {
        model,
        lights = model.GetComponentsInChildren<LightTracked>()
          // .Where(l => {
          //   var hasSelectedLights = selLights.Length == 0 || selLights.Any(sl => sl.gameObject.GetInstanceID() == l.gameObject.GetInstanceID());
          //   return hasSelectedLights;
          // })
          // .ToArray()
      })
      .ToArray()
    ;

    var levelModelsToBake = allLevelModels.Where(l => {
      if (selLights.Length == 0) return true;

      var hasSelectedLights = selLights.Any(sl => l.lights.Any(j => j.gameObject.GetInstanceID() == sl.gameObject.GetInstanceID()));

      return hasSelectedLights;
      // var hasSelectedLights = selLights.Length == 0 || selLights.Any(sl => sl.gameObject.GetInstanceID() == l.model.gameObject.GetInstanceID());
      // return hasSelectedLights;
    }).ToArray();

    ClearLightmaps(levelModelsToBake.Select(lm => lm.model.GetInstanceID()).ToArray());

    // foreach (var ml in levelModels) {
    //   ClearLightmaps()
    //   // ToggleContributeGi(ml.model.gameObject, false);
    // }

    levelModelsToBake = levelModelsToBake.Where(param => param.lights.Length > 0).ToArray();

    Debug.Log($"{levelModelsToBake.Length} rooms with lights detected");
    if (levelModelsToBake.Length == 0) return;

    // turn all level lightmaps we want to bake on so we can rebuild them;
    foreach (var ml in levelModelsToBake) {
      ToggleContributeGi(ml.model.gameObject, true);
    }
    CSGModelManager.BuildLightmapUvs();
    // then turn everything back off so we only bake one at a time
    foreach (var ml in allLevelModels) {
      // ToggleContributeGi(ml.model.gameObject, true);
      foreach (var light in ml.lights) {
        light.BakedLight.enabled = false;
      }
      ml.model.SetActive(false);
    }

    var bakeComplete = Observable.FromEvent(
       handler => Lightmapping.bakeCompleted += handler,
       handler => Lightmapping.bakeCompleted -= handler
    );

    var roomBakes = levelModelsToBake.Select(group => {
      var lightBakes = Observable.Concat(group.lights.Select(light => {
        light.BakedLight.enabled = true;
        // ToggleContributeGi(group.model.gameObject, true);
        group.model.gameObject.SetActive(true);

        Debug.Log($"baking {light.name} ({light.GetInstanceID()}) in {group.model.name}");

        Lightmapping.BakeAsync();

        return bakeComplete
          .Take(1)
          .Select(_ =>  {
            var meshRenderers = group.model.GetComponentsInChildren<MeshRenderer>().Where(mr => mr.name.Equals("[generated-render-mesh]")).ToArray();
            var meshLightmapData = meshRenderers.Select(mr => new MeshLightmapData() {
              owner = mr,
              lightmapIndex = mr.lightmapIndex,
              lightmapScaleOffset = mr.lightmapScaleOffset,
              test = mr.gameObject.GetComponent<MeshFilter>()
            }).ToArray();
            
            return new LightmapGroup() {
              Light = light,
              lightColor = light.Color,
              lightLit = light.Lit,
              lightPosition = light.transform.position,
              roomModelId = group.model.GetInstanceID(),
              lightmaps = LightmapSettings.lightmaps.Select(lm => MakeReadableCopy(lm.lightmapColor)).ToArray(),
              meshLightmapData = meshLightmapData
            };
          }
          )
          .Do(_ => {
            Debug.Log($"baked {light.name} ({light.GetInstanceID()}) in {group.model.name}");
            light.BakedLight.enabled = false;
          })
        ;

      }));

      return lightBakes.Do(_ => {}, _ => {},
        // complete => { ToggleContributeGi(group.model.gameObject, false); }
        complete => { group.model.gameObject.SetActive(false); }
      );

    });

    var bake = Observable.Concat(roomBakes);

    var list = new List<LightmapGroup>();
    // var db = Resources.FindObjectsOfTypeAll<LightmapDb>().First();
    bake.Subscribe(
      lightmaps => {
        // db.upsert(lightmaps);
        list.Add(lightmaps);
        ExportGroupLightmaps(lightmaps);
      },
      error => Debug.LogError($"Error during baking: {error}"),
      // completed => Debug.Log("test")
      completed => {
        Debug.Log("All bakes completed!");
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

        var db = Resources.FindObjectsOfTypeAll<LightmapDb>().First();
        db.upsert(list.ToArray());
        // Resources.FindObjectsOfTypeAll<LightmapDb>().First().lightmapGroups = list.ToArray();

        foreach (var ml in allLevelModels) {
          // ToggleContributeGi(ml.model.gameObject, true);
          ml.model.gameObject.SetActive(true);
        }

      }
    );
  }

  static void ExportGroupLightmaps(LightmapGroup group) {
    int idx = 0;
    string lightmapsPath = Path.Combine(Application.dataPath, "Resources/Textures/lightmaps");

    if (!AssetDatabase.IsValidFolder($"Assets/Resources/Textures/lightmaps/{group.roomModelId}")) {
      AssetDatabase.CreateFolder("Assets/Resources/Textures/lightmaps", group.roomModelId.ToString());
    }

    foreach (var texture in group.lightmaps) {
      Texture2D readableTexture = MakeReadableCopy(texture);
      byte[] pngData = readableTexture.EncodeToPNG();

      if (pngData != null) {

        var fileName = $"{group.Light.GetInstanceID()}_{idx++}.png";
        var writePath = Path.Combine(lightmapsPath, group.roomModelId.ToString(), fileName);

        File.WriteAllBytes(writePath, pngData);
      }

      GameObject.DestroyImmediate(readableTexture);
    }
  }

  private static Texture2D MakeReadableCopy(Texture2D source) {
    // Create a temporary RenderTexture of the same size as the original texture
    RenderTexture tmp = RenderTexture.GetTemporary(
      source.width,
      source.height,
      0,
      RenderTextureFormat.Default,
      RenderTextureReadWrite.Linear
    );

    // Blit the pixels on texture to the RenderTexture
    Graphics.Blit(source, tmp);

    // Backup the currently set RenderTexture
    RenderTexture previous = RenderTexture.active;

    // Set the current RenderTexture to the temporary one we created
    RenderTexture.active = tmp;

    // Create a new readable Texture2D to copy the pixels to it
    Texture2D readableTexture = new Texture2D(source.width, source.height);

    // Copy the pixels from the RenderTexture to the new Texture
    readableTexture.ReadPixels(new Rect(0, 0, tmp.width, tmp.height), 0, 0);
    readableTexture.Apply();

    // Reset the active RenderTexture
    RenderTexture.active = previous;

    // Release the temporary RenderTexture
    RenderTexture.ReleaseTemporary(tmp);

    return readableTexture;
  }

  [MenuItem("Level/Clear Lightmap DB")]
  public static void ClearLightmapDb() {
    var lightmapDb = Resources.FindObjectsOfTypeAll<LightmapDb>().First();
    ClearLightmaps();
    lightmapDb.lightmapGroups = null;
  }

  [MenuItem("Level/DetectDirtyLights")]
  public static LightTracked[] DetectDirtyLights() {
    // @todo - check if room model changed?
    // see InternalRealtimeCSG.EditModeCommonGUI.NeedLightmapUVUpdate
    var lights = GameObject.FindObjectsOfType<LightTracked>();

    var lightmapDb = Resources.FindObjectsOfTypeAll<LightmapDb>().First().lightmapGroups;

    if (lightmapDb == null) {
      return lights;
    }

    var dirtyLights = lights.Where(light => {
      var savedData = lightmapDb.FirstOrDefault(sd => sd.Light.GetInstanceID() == light.GetInstanceID());
      if (savedData == null) 
        return true;

      var isDirty = light.Color != savedData.lightColor
        || light.transform.position != savedData.lightPosition
        || light.Lit != savedData.lightLit
      ;

      return isDirty;
    });

    return dirtyLights.ToArray();
  }
} 