using System.Collections;
using UnityEngine;

namespace ExoFlight.Triggers
{
    [CreateAssetMenu(menuName = "ExoFlight/Triggers/Debug Log Action")]
    public sealed class DebugLogAction : TriggerAction
    {
        [TextArea(2, 6)] [SerializeField] string message = "Trigger reached.";

        public override IEnumerator Execute(TriggerContext context)
        {
            Debug.Log(message, context.Zone);
            context.Complete();
            yield break;
        }
    }
}
