using UnityEngine;
using UnityEngine.UI;

namespace ExoFlight.Cargo.UI
{
    /// <summary>The same card layout is used in world space and on the camera canvas.</summary>
    public sealed class CargoUiCard : MonoBehaviour
    {
        public Text heading;
        public Text status;
        public Text detail;
        public Image accent;
        public Image progressFill;

        public void Show(string title, string message, string description, float progress, Color color)
        {
            heading.text = title;
            heading.color = color;
            status.text = message;
            detail.text = description;
            accent.color = color;
            progressFill.color = color;
            var rect = progressFill.rectTransform;
            rect.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
        }
    }
}
