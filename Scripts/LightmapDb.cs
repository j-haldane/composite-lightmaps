using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[CreateAssetMenu(fileName = "LightmapDb", menuName = "ScriptableObjects/LightmapDb")]
public class LightmapDb: ScriptableObject {
  [SerializeField] public LightmapGroup[] lightmapGroups;

  public void upsert(LightmapGroup[] groups) {


    var upserted = lightmapGroups == null ? new List<LightmapGroup>() : lightmapGroups.ToList();
    foreach (var group in groups) {
      var found = upserted.Find(g => g.roomModelId == group.roomModelId && g.Light.GetInstanceID() == group.Light.GetInstanceID());
      if (found != null) {
        upserted.Remove(found);
      }
      upserted.Add(group);
    }

    lightmapGroups = upserted.ToArray();
  }
}