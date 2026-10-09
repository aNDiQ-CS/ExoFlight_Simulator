using System.Collections;
using UnityEngine;

namespace ExoFlight.Triggers
{
    [CreateAssetMenu(menuName = "ExoFlight/Triggers/Dialogue Action")]
    public sealed class DialogueAction : TriggerAction
    {
        [SerializeField] DialogueDefinition dialogue;

        public override bool CanExecute(TriggerContext context, out string error)
        {
            if (dialogue == null || context.Dialogue == null || !context.Dialogue.isActiveAndEnabled)
            {
                error = "Assign a dialogue asset and an active Dialogue Player.";
                return false;
            }
            if (context.Dialogue.IsPlaying)
            {
                error = "Dialogue Player is already playing.";
                return false;
            }
            return dialogue.IsValid(out error);
        }

        public override IEnumerator Execute(TriggerContext context)
        {
            yield return context.Dialogue.Play(dialogue);
            if (context.Dialogue.CompletedNormally)
                context.Complete();
        }
    }
}
