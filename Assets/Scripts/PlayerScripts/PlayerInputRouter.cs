using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

// The player's one input instance. Gameplay, menu, UI and debug input all read from here.
[DefaultExecutionOrder(-1000)]
public class PlayerInputRouter : MonoBehaviour
{
    private const string OverridesKey = "input.bindingOverrides";

#if UNITY_EDITOR || STATUS_DEBUG
    private const bool DebugBuild = true;
#else
    private const bool DebugBuild = false;
#endif

    [Tooltip("Debug map. Only ever on in editor and STATUS_DEBUG builds.")]
    [SerializeField] private bool debugKeys = true;

    private PlayerInputs input;
    private readonly HashSet<object> uiLocks = new HashSet<object>();

    // The local player's router, for scripts that don't live on the player.
    public static PlayerInputRouter Local { get; private set; }

    // True on entering UI mode, false on leaving it.
    public event Action<bool> UIModeChanged;

    public PlayerInputs Inputs { get { Init(); return input; } }
    public PlayerInputs.PlayerActions Player => Inputs.Player;
    public PlayerInputs.UIActions UI => Inputs.UI;
    public PlayerInputs.MenuActions Menu => Inputs.Menu;
    public PlayerInputs.DebugActions Debug => Inputs.Debug;

    public bool InUI => uiLocks.Count > 0;
    public bool DebugEnabled => DebugBuild && debugKeys;

    // Router above the component, else the local player's.
    public static PlayerInputRouter For(Component c)
    {
        var r = c != null ? c.GetComponentInParent<PlayerInputRouter>() : null;
        return r != null ? r : Local;
    }

    private void Awake()
    {
        Init();
        if (Local == null) Local = this;
        else if (Local != this) UnityEngine.Debug.LogWarning("[Input] More than one PlayerInputRouter; Local stays on the first.");
    }

    // Lazy so readers can grab actions in their own Awake regardless of order.
    private void Init()
    {
        if (input != null) return;

        // Alt+1 must not also fire 1. Same as Project Settings > Input System > Enable Input Consumption.
        InputSystem.settings.shortcutKeysConsumeInput = true;

        input = new PlayerInputs();
        LoadBindingOverrides();
    }

    private void OnEnable() => ApplyMode();

    private void Start()
    {
        BindUIModule();
        ApplyMode();
    }

    private void OnDisable()
    {
        if (input != null) input.Disable();
    }

    private void OnDestroy()
    {
        if (input != null)
        {
            input.Disable();
            input.Dispose();
            input = null;
        }
        if (Local == this) Local = null;
    }

    // ------------------------------------------------------------------ mode

    // Any object can hold UI mode (a menu, a dialog); gameplay input returns when the last lets go.
    public void PushUI(object owner)
    {
        if (owner == null || !uiLocks.Add(owner) || uiLocks.Count != 1) return;
        BindUIModule();
        ApplyMode();
        UIModeChanged?.Invoke(true);
    }

    public void PopUI(object owner)
    {
        if (owner == null || !uiLocks.Remove(owner) || uiLocks.Count != 0) return;
        ApplyMode();
        UIModeChanged?.Invoke(false);
    }

    // Disabling a map cancels its in-progress actions, so movement and sprint drop on their own.
    private void ApplyMode()
    {
        if (input == null || !isActiveAndEnabled) return;

        bool ui = InUI;
        if (ui) { input.Player.Disable(); input.UI.Enable(); }
        else { input.UI.Disable(); input.Player.Enable(); }

        input.Menu.Enable();
        if (DebugEnabled) input.Debug.Enable();
        else input.Debug.Disable();

        Cursor.lockState = ui ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = ui;
    }

    // Menu navigation reads (and rebinds with) this asset's UI map.
    private void BindUIModule()
    {
        var module = FindFirstObjectByType<InputSystemUIInputModule>();
        if (module != null && module.actionsAsset != input.asset)
            module.actionsAsset = input.asset;
    }

    // ------------------------------------------------------------------ bindings

    public void LoadBindingOverrides()
    {
        string json = PlayerPrefs.GetString(OverridesKey, "");
        if (!string.IsNullOrEmpty(json)) Inputs.asset.LoadBindingOverridesFromJson(json);
    }

    public void SaveBindingOverrides()
    {
        PlayerPrefs.SetString(OverridesKey, Inputs.asset.SaveBindingOverridesAsJson());
        PlayerPrefs.Save();
    }

    public void ResetBindingOverrides()
    {
        Inputs.asset.RemoveAllBindingOverrides();
        PlayerPrefs.DeleteKey(OverridesKey);
    }
}
