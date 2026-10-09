using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace ExoFlight.Triggers
{
    public sealed class TriggerSequence : MonoBehaviour
    {
        [Serializable]
        public sealed class Step
        {
            public TriggerZone zone;
            public TriggerAction action;
        }

        [Tooltip("Order here is the required order of entering the zones.")]
        [SerializeField] List<Step> steps = new List<Step>();
        [SerializeField] DialoguePlayer dialoguePlayer;
        [SerializeField] UnityEvent completed = new UnityEvent();
        public int NextStepIndex { get; private set; }
        public bool IsRunning { get; private set; }
        public bool IsComplete => steps.Count > 0 && NextStepIndex == steps.Count;
        public int StepCount => steps.Count;
        bool valid;
        Coroutine actionRoutine;

        void Awake()
        {
            var zones = new HashSet<TriggerZone>();
            valid = steps.Count > 0;
            foreach (var step in steps)
            {
                if (step == null || step.zone == null || step.action == null || !zones.Add(step.zone))
                {
                    valid = false;
                    Debug.LogError("Sequence needs a unique zone and an action for every step.", this);
                    return;
                }
            }
            foreach (var step in steps)
                step.zone.Bind(this);
        }

        public bool TryActivate(TriggerZone zone, PlayerCheckpoint player)
        {
            if (!isActiveAndEnabled || !valid || IsRunning || IsComplete || player == null
                || steps[NextStepIndex].zone != zone)
                return false;

            var context = new TriggerContext(player, zone, dialoguePlayer);
            var action = steps[NextStepIndex].action;
            if (!action.CanExecute(context, out string error))
            {
                Debug.LogWarning($"[Sequence] {error}", this);
                return false;
            }

            // Lock before starting a coroutine, including for instant actions.
            IsRunning = true;
            actionRoutine = StartCoroutine(RunStep(action, context));
            return true;
        }

        IEnumerator RunStep(TriggerAction action, TriggerContext context)
        {
            // Even an instant action remains locked for the rest of this physics frame.
            yield return null;
            yield return action.Execute(context);
            if (context.Succeeded)
            {
                NextStepIndex++;
                Debug.Log($"[Sequence] Completed {NextStepIndex}/{steps.Count}: {context.Zone.name}", this);
            }
            IsRunning = false;
            actionRoutine = null;
            if (IsComplete)
                completed.Invoke();
        }

        void OnDisable()
        {
            if (actionRoutine != null)
            {
                StopCoroutine(actionRoutine);
                if (dialoguePlayer != null)
                    dialoguePlayer.Stop();
            }
            actionRoutine = null;
            IsRunning = false;
        }
    }
}
