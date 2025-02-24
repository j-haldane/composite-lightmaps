using UnityEngine;

[CreateAssetMenu(fileName = "MainSettings", menuName = "ScriptableObjects/MainSettings", order = 0)]
public class MainSettings : ScriptableObject {

  public LightingPreset LightingPreset = LightingPreset.BAKED;
  public bool PropsEnabled = false;
  public Color GameModeAmbientLightColor = Color.black;
}

public enum LightingPreset {
  BAKED = 0,
  VERTEX = 1,
}
