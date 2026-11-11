using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Drives the Animation Versions screen: shows the selected entry from the catalog (ordered by
/// character, then — within a character — its own "Idle rotation" entry first if it has one, then
/// by action and version — the same order navigation moves through), with a "sheet finishing"
/// placeholder card in place of the sprite for a pending entry.
///
/// Three nested levels of navigation, each wrapping at its own ends:
/// - Left/right (or the ◀ ▶ buttons) step one version at a time through the whole catalog.
/// - Up/down (or a second row of buttons) jump to the first version of the next/previous distinct
///   action within the *current* character only.
/// - PageUp/PageDown (or a third row of buttons) jump to the first entry of the next/previous
///   distinct character.
///
/// An entry with a non-empty <see cref="AnimationVersionEntry.status"/> (e.g. "prod candidate")
/// shows it as a badge next to the title; the badge is hidden entirely for an entry with no status
/// set. An entry with <see cref="AnimationVersionEntry.isRotation"/> set hands off to
/// <see cref="IdleRotationPlayer"/> instead of the normal single-sprite player, driven by that same
/// character's idle/fidget entries named in its rotationIdleVersion/rotationFidgetActions/
/// rotationFidgetVersions fields.
/// </summary>
public class AnimationVersionsController : MonoBehaviour
{
    [SerializeField] private AnimationVersionsCatalog catalog;
    [SerializeField] private AnimationVersionPlayer player;
    [Tooltip("Drives a character's own \"Idle rotation\" entry — see AnimationVersionEntry.isRotation.")]
    [SerializeField] private IdleRotationPlayer rotationPlayer;
    [SerializeField] private GameObject pendingCard;
    [SerializeField] private Text titleLabel;
    [SerializeField] private Text descriptionLabel;
    [SerializeField] private Text counterLabel;
    [Tooltip("The badge shown next to the title for an entry with a non-empty status (e.g. \"prod candidate\") — hidden entirely when status is empty.")]
    [SerializeField] private GameObject statusBadge;
    [SerializeField] private Text statusBadgeLabel;

    // The description's own y position (anchored from the top) shifts down to make room only
    // when the badge is actually showing — otherwise a two-line description on a badge-less
    // entry (e.g. idle v8) loses enough headroom to clip the plume on its own second line.
    private const float DescriptionYWithoutBadge = -78f;
    private const float DescriptionYWithBadge = -108f;

    private int _index;

    private void Start()
    {
        _index = 0;
        Show();
    }

    private void Update()
    {
        if (Keyboard.current == null) return;
        if (Keyboard.current.leftArrowKey.wasPressedThisFrame) Step(-1);
        if (Keyboard.current.rightArrowKey.wasPressedThisFrame) Step(1);
        if (Keyboard.current.upArrowKey.wasPressedThisFrame) StepAction(1);
        if (Keyboard.current.downArrowKey.wasPressedThisFrame) StepAction(-1);
        if (Keyboard.current.pageUpKey.wasPressedThisFrame) StepCharacter(1);
        if (Keyboard.current.pageDownKey.wasPressedThisFrame) StepCharacter(-1);
    }

    /// <summary>Moves by <paramref name="delta"/> versions through the whole catalog, wrapping.
    /// Bound to the ◀ ▶ buttons as well as the left/right arrow keys.</summary>
    public void Step(int delta)
    {
        int count = catalog.Entries.Count;
        _index = ((_index + delta) % count + count) % count;
        Show();
    }

    /// <summary>Jumps to the first version of the next/previous distinct action within the current
    /// character only, wrapping.</summary>
    public void StepAction(int delta)
    {
        string character = catalog.Entries[_index].character;
        var actionsForCharacter = catalog.Entries
            .Where(e => e.character == character)
            .Select(e => e.action)
            .Distinct()
            .ToList();

        int actionIndex = actionsForCharacter.IndexOf(catalog.Entries[_index].action);
        int count = actionsForCharacter.Count;
        actionIndex = ((actionIndex + delta) % count + count) % count;
        string targetAction = actionsForCharacter[actionIndex];

        _index = catalog.Entries.ToList().FindIndex(e => e.character == character && e.action == targetAction);
        Show();
    }

    /// <summary>Jumps to the first entry of the next/previous distinct character, wrapping.</summary>
    public void StepCharacter(int delta)
    {
        var characters = catalog.Entries.Select(e => e.character).Distinct().ToList();
        int characterIndex = characters.IndexOf(catalog.Entries[_index].character);
        int count = characters.Count;
        characterIndex = ((characterIndex + delta) % count + count) % count;
        string targetCharacter = characters[characterIndex];

        _index = catalog.Entries.ToList().FindIndex(e => e.character == targetCharacter);
        Show();
    }

    private void Show()
    {
        AnimationVersionEntry entry = catalog.Entries[_index];
        string displayCharacter = Capitalize(entry.character);

        titleLabel.text = entry.isRotation
            ? $"{displayCharacter} · Idle rotation"
            : $"{displayCharacter} · {entry.action} · v{entry.version}";
        descriptionLabel.text = entry.description;
        counterLabel.text = $"{_index + 1} / {catalog.Entries.Count}";

        bool hasStatus = !string.IsNullOrEmpty(entry.status);
        statusBadge.SetActive(hasStatus);
        if (hasStatus) statusBadgeLabel.text = entry.status.ToUpperInvariant();

        RectTransform descriptionRect = descriptionLabel.rectTransform;
        descriptionRect.anchoredPosition = new Vector2(
            descriptionRect.anchoredPosition.x, hasStatus ? DescriptionYWithBadge : DescriptionYWithoutBadge);

        if (entry.isRotation)
        {
            pendingCard.SetActive(false);
            player.Stop();
            AnimationVersionEntry idleEntry = FindEntry(entry.character, "idle", entry.rotationIdleVersion);
            var fidgets = new AnimationVersionEntry[entry.rotationFidgetActions.Length];
            for (int i = 0; i < fidgets.Length; i++)
            {
                fidgets[i] = FindEntry(entry.character, entry.rotationFidgetActions[i], entry.rotationFidgetVersions[i]);
            }
            rotationPlayer.Begin(idleEntry, fidgets);
            return;
        }

        rotationPlayer.Stop();
        bool pending = entry.pending;
        pendingCard.SetActive(pending);
        if (pending)
        {
            player.Stop();
        }
        else
        {
            player.Play(entry);
        }
    }

    private AnimationVersionEntry FindEntry(string character, string action, int version)
    {
        foreach (AnimationVersionEntry entry in catalog.Entries)
        {
            if (entry.character == character && entry.action == action && entry.version == version) return entry;
        }
        Debug.LogError($"AnimationVersionsController: no catalog entry for \"{character}\" \"{action}\" v{version}.");
        return default;
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
}
