using UnityEngine;
using System.Linq;
using PlasticGui;
using UnityEngine.Events;

namespace Unity.FPS.Game {
  [RequireComponent(typeof(Light))]
  public class LightTracked : MonoBehaviour {

    public Light BakedLight {
      get { return this.bakedLight; }
    }
    public Light VertexLight {
      get { return this.vertexLight; }
    }

    public Color Color {
      get { return this.color; }
      set {
        this.BakedLight.color = value;
        this.VertexLight.color = value;
        this.color = value;
        this.OnDirty?.Invoke();
      }
    }

    public float Intensity {
      get { return this.intensity; }
      set {
        this.intensity = value;
        this.BakedLight.intensity = value;
        this.VertexLight.intensity = value;
        this.OnDirty?.Invoke();
      }
    }

    public bool Lit {
      get { return this._lit; }
      private set { this._lit = value; }
    }

    [SerializeField] private Light bakedLight;
    [SerializeField] private Light vertexLight;
    [SerializeField] private TrackedLightSet LightSet;
    [SerializeField] private Interactable interact;
    private Color color;
    private float intensity;

    public UnityAction OnDirty;
    private bool _lit;

    public void OnEnable() {
      this.LightSet.Add(this);

      this.Lit = this.VertexLight.enabled;
      this.BakedLight.enabled = this.Lit;

      this.Color = this.BakedLight.color;
      this.Intensity = this.BakedLight.intensity;

      if (this.interact != null) {
        this.interact.OnFrob += (Actor a) => {
          this.ToggleLit(null);
        };
      }
    }

    public void OnDisable() {
      this.LightSet.Remove(this);
    }

    [ContextMenu("SetRandomColor")]
    public void SetRandomColor() {
      var color = Random.ColorHSV(0f, 1f, 1f, 1f, 1f, 1f, 1f, 1f);
      this.Color = color;
    }

    public void ToggleLit(bool? lit) {
      bool dirtied = false;

      if (lit == null) {
        this.Lit = !this.Lit;
        dirtied = true;
      } else {
        this.Lit = (bool)lit;
        if (this.Lit != (bool)lit) {
          dirtied = true;
        }
      }

      if (dirtied) {
        this.OnDirty?.Invoke();
      }

      this.BakedLight.enabled = this.Lit;
      this.VertexLight.enabled = this.Lit;
    }
  }
}