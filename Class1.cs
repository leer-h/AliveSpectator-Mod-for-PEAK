using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using UnityEngine.InputSystem;
using Zorro.ControllerSupport;

[BepInPlugin("com.leer.alivespectator", "Alive Spectator", "1.3.0")]
public class AliveSpectator : BaseUnityPlugin
{
    private static bool manualSpectateActive;

    private MainCameraMovement cameraMovement;

    private ConfigEntry<KeyCode> toggleKey;
    private ConfigEntry<KeyCode> gamepadKey1;
    private ConfigEntry<KeyCode> gamepadKey2;

    private static MethodInfo resetInputMethod;

    private void Awake()
    {
        toggleKey = Config.Bind("Binds", "KeyboardToggle", KeyCode.F7, "Keyboard key for toggling spectator mode on/off");
        gamepadKey1 = Config.Bind("Binds", "GamepadToggle1", KeyCode.JoystickButton4, "First gamepad button for toggle activation (e.g., LB)");
        gamepadKey2 = Config.Bind("Binds", "GamepadToggle2", KeyCode.JoystickButton5, "Second gamepad button for toggle activation (e.g., RB)");

        Harmony harmony = new Harmony("com.leer.alivespectator");
        harmony.PatchAll(Assembly.GetExecutingAssembly());

        resetInputMethod = typeof(CharacterInput).GetMethod("ResetInput", BindingFlags.Instance | BindingFlags.NonPublic);
    }

    private void Update()
    {
        if (!IsTogglePressed())
            return;

        if (Character.localCharacter == null || Character.localCharacter.data == null || Character.localCharacter.data.fullyPassedOut)
            return;

        if (cameraMovement == null)
            cameraMovement = MainCameraMovement.Instance;

        if (cameraMovement == null)
            return;

        manualSpectateActive = !manualSpectateActive;
    }

    private bool IsTogglePressed()
    {
        bool keyboard = Input.GetKeyDown(toggleKey.Value);
        bool gamepadCombo =
            (Input.GetKeyDown(gamepadKey1.Value) && Input.GetKey(gamepadKey2.Value)) ||
            (Input.GetKeyDown(gamepadKey2.Value) && Input.GetKey(gamepadKey1.Value));
        return keyboard || gamepadCombo;
    }

    public static bool IsManualSpectating() => manualSpectateActive;

    [HarmonyPatch(typeof(MainCameraMovement), "LateUpdate")]
    class Patch_LateUpdate
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);

            var localCharacterField = AccessTools.Field(typeof(Character), "localCharacter");
            var dataField = AccessTools.Field(typeof(Character), "data");
            var fullyPassedOutField = AccessTools.Field(typeof(CharacterData), "fullyPassedOut");

            var isManualSpectatingGetter = AccessTools.Method(typeof(AliveSpectator), nameof(IsManualSpectating));

            for (int i = 0; i < codes.Count - 4; i++)
            {
                if (codes[i].LoadsField(localCharacterField) &&
                    codes[i + 1].LoadsField(dataField) &&
                    codes[i + 2].LoadsField(fullyPassedOutField))
                {
                    codes.InsertRange(i + 3, new[]
                    {
                        new CodeInstruction(OpCodes.Call, isManualSpectatingGetter),
                        new CodeInstruction(OpCodes.Or)
                    });

                    break;
                }
            }

            return codes;
        }
    }

    [HarmonyPatch(typeof(CharacterInput), "Sample")]
    class Patch_CharacterInput_Sample
    {
        static void Prefix(ref bool playerMovementActive)
        {
            if (IsManualSpectating())
            {
                playerMovementActive = false;
            }
        }

        static void Postfix(CharacterInput __instance)
        {
            if (!IsManualSpectating())
                return;

            resetInputMethod?.Invoke(__instance, null);

            if (CharacterInput.action_look != null)
                __instance.lookInput = CharacterInput.action_look.ReadValue<Vector2>();
            else
                __instance.lookInput = Vector2.zero;


            if (InputHandler.GetCurrentUsedInputScheme() == InputScheme.KeyboardMouse)
            {
                __instance.spectateLeftWasPressed = __instance.HotbarKeyWasPressed(0);
                __instance.spectateRightWasPressed = __instance.HotbarKeyWasPressed(1);
            }
            else
            {
                __instance.spectateLeftWasPressed = CharacterInput.action_selectSlotBackward?.IsPressed() ?? false;
                __instance.spectateRightWasPressed = CharacterInput.action_selectSlotForward?.IsPressed() ?? false;
            }

            float scroll = 0f;

            scroll += Input.mouseScrollDelta.y;

            if (Gamepad.current != null)
            {
                float buttonZoomSpeed = 30f;
                if (Gamepad.current.leftShoulder.isPressed)
                    scroll -= buttonZoomSpeed * Time.deltaTime;
                if (Gamepad.current.rightShoulder.isPressed)
                    scroll += buttonZoomSpeed * Time.deltaTime;
            }

            __instance.scrollInput = scroll;

            __instance.scrollForwardIsPressed = Gamepad.current?.rightShoulder.isPressed ?? false;
            __instance.scrollBackwardIsPressed = Gamepad.current?.leftShoulder.isPressed ?? false;
        }
    }
}
