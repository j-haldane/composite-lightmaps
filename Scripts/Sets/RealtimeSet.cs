using System.Collections.Generic;
using UnityEngine;

namespace Unity.FPS.Game {
    public abstract class RealtimeSet<T> : ScriptableObject {

        public List<T> Items = new List<T>();

        public virtual bool Add(T t) {

            bool added = false;

            if (!Items.Contains(t)) {
                added = true;
                Items.Add(t);
            }

            return added;
        }

        public void Remove(T t) {
            if (Items.Contains(t)) {
                Items.Remove(t);
            }
        }
    }
}