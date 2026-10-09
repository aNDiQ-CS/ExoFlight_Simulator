using System.Collections;
using UnityEngine;

namespace ExoFlight.Triggers
{
    /// <summary>Reusable action configuration. Runtime progress belongs to TriggerSequence.</summary>
    public abstract class TriggerAction : ScriptableObject
    {
        public virtual bool CanExecute(TriggerContext context, out string error)
        {
            error = null;
            return true;
        }

        public abstract IEnumerator Execute(TriggerContext context);
    }

    public sealed class TriggerContext
    {
        public PlayerCheckpoint Player { get; }
        public TriggerZone Zone { get; }
        public DialoguePlayer Dialogue { get; }
        public bool Succeeded { get; private set; }

        public TriggerContext(PlayerCheckpoint player, TriggerZone zone, DialoguePlayer dialogue)
        {
            Player = player;
            Zone = zone;
            Dialogue = dialogue;
        }

        public void Complete() => Succeeded = true;
    }
}
