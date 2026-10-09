using System.Collections;
using UnityEngine;

namespace ExoFlight.Triggers
{
    [CreateAssetMenu(menuName = "ExoFlight/Triggers/Set Checkpoint Action")]
    public sealed class SetCheckpointAction : TriggerAction
    {
        public override bool CanExecute(TriggerContext context, out string error)
        {
            error = context.Zone.Checkpoint == null ? "Assign the zone's Checkpoint transform." : null;
            return error == null;
        }

        public override IEnumerator Execute(TriggerContext context)
        {
            context.Player.Save(context.Zone.Checkpoint);
            Debug.Log($"[Checkpoint] Saved {context.Zone.Checkpoint.name}", context.Zone);
            context.Complete();
            yield break;
        }
    }
}
