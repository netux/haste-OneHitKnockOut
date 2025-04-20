using Landfall.Haste;
using Landfall.Modding;
using UnityEngine.Localization;
using Zorro.Settings;
using MonoMod.Cil;
using Mono.Cecil.Cil;
using System.Reflection;

namespace HasteOHKOMod;

[LandfallPlugin]
public class OneHitKnockOut
{
    private static bool isTriggeringOHKOPayloadAlready = false;

    public static bool IsOHKOEnabled
    {
        get => GameHandler.Instance.SettingsHandler.GetSetting<OneHitKnockOutEnabledSetting>().Value == OffOnMode.ON;
    }

    public static OneHitKnockoutOnHit OnHit
    {
        get => GameHandler.Instance.SettingsHandler.GetSetting<OneHitKnockOutOnHitSetting>().Value;
    }

    static OneHitKnockOut()
    {
        // TODO(netux): show a counter of how many automatic restarts there were

        On.PlayerCharacter.Start += (original, playerCharacter) =>
        {
            isTriggeringOHKOPayloadAlready = false;

            return original(playerCharacter);
        };

        On.HasteStats.AddStat += (original, statType, value) =>
        {
            original(statType, value);

            if (statType != HasteStatType.STAT_DAMAGE_TAKEN)
            {
                return;
            }

            if (!IsOHKOEnabled)
            {
                return;
            }

            if (isTriggeringOHKOPayloadAlready)
            {
                return;
            }
            isTriggeringOHKOPayloadAlready = true;

            switch (OnHit)
            {
                case OneHitKnockoutOnHit.END_RUN:
                    RunHandler.LoseRun(transitionOutOverride: false);
                    break;
                case OneHitKnockoutOnHit.RESTART_SAME_SHARD:
                case OneHitKnockoutOnHit.RESTART_NEW_SHARD:
                    var config = RunHandler.config;
                    var shardID = RunHandler.RunData.shardID;
                    var seed = OnHit == OneHitKnockoutOnHit.RESTART_SAME_SHARD
                        ? RunHandler.RunData.currentSeed
                        : RunHandler.GenerateSeed();

                    //RunHandler.LoseRun(transitionOutOverride: true);
                    RunHandler.ClearCurrentRun();
                    RunHandler.StartAndPlayNewRun(config, shardID, seed);
                    break;
            }
        };

        var UI_HealthBarBarField = typeof(UI_HealthBar).GetField("bar", BindingFlags.Instance | BindingFlags.NonPublic);
        var UniversalBarSetValuesMethod = typeof(UniversalBar).GetMethod("SetValues", BindingFlags.Instance | BindingFlags.NonPublic);
        var PlayerHealthPercentageMethod = typeof(Player).GetMethod("HealthPercentage", BindingFlags.Instance | BindingFlags.NonPublic);
        On.UI_HealthBar.Update += (original, healthBar) =>
        {
            if (!IsOHKOEnabled)
            {
                original(healthBar);
                return;
            }

            var bar = (UniversalBar)UI_HealthBarBarField.GetValue(healthBar);
            var healthPercentage = (float)PlayerHealthPercentageMethod.Invoke(Player.localPlayer, []);
            UniversalBarSetValuesMethod.Invoke(bar, [
                /* fills: */ healthPercentage < 1f ? 0f : 1f,
                /* segments: */ 1f,
                /* activationAmount: */ Player.localPlayer.data.HealingFeedback()
            ]);
        };

        IL.UI_PlayerLives.MaxLivesChanged += (il) =>
        {
            //UnityEngine.Debug.Log("Patching UI_PlayerLives.MaxLivesChanged");

            ILCursor cursor = new(il);

            cursor.GotoNext(
                MoveType.After,
                i => i.MatchLdfld(typeof(PlayerStats).GetField("lives", BindingFlags.Instance | BindingFlags.Public)),
                i => i.MatchCallvirt(typeof(PlayerStat).GetMethod("GetValueInt", BindingFlags.Instance | BindingFlags.NonPublic))
            );

            // if (OneHitKnockOut.IsOHKOEnabled) { use lives from PlayerStats }
            // else { use constant of 1 life heart }
            var storeOriginalLivesLabel = cursor.DefineLabel();
            cursor.Emit(OpCodes.Call, typeof(OneHitKnockOut).GetMethod("get_IsOHKOEnabled", BindingFlags.Static | BindingFlags.Public));
            cursor.Emit(OpCodes.Brfalse_S, storeOriginalLivesLabel);
            cursor.Emit(OpCodes.Pop); // pop result of PlayerStat.GetValueInt()
            cursor.Emit(OpCodes.Ldc_I4, 1); // add our own value instead :>
            cursor.MarkLabel(storeOriginalLivesLabel);

            //DebugLogInstructions(cursor.Instrs);
        };

        IL.UI_PlayerLives.LivesChanged += (il) =>
        {
            //UnityEngine.Debug.Log("Patching UI_PlayerLives.LivesChanged");

            ILCursor cursor = new(il);

            cursor.GotoNext(
                MoveType.After,
                i => i.MatchLdfld(typeof(PersistentPlayerData).GetField("lives", BindingFlags.Instance | BindingFlags.Public))
            );

            var storeOriginalLivesLabel = cursor.DefineLabel();
            cursor.Emit(OpCodes.Call, typeof(OneHitKnockOut).GetMethod("get_IsOHKOEnabled", BindingFlags.Static | BindingFlags.Public));
            cursor.Emit(OpCodes.Brfalse_S, storeOriginalLivesLabel);
            cursor.Emit(OpCodes.Pop); // pop result of PlayerStat.GetValueInt()
            cursor.Emit(OpCodes.Ldc_I4, 1); // add our own value instead :>
            cursor.MarkLabel(storeOriginalLivesLabel);

            //DebugLogInstructions(cursor.Instrs);
        };
    }

    static void DebugLogInstructions(IEnumerable<Instruction> instrs)
    {
        foreach (var instruction in instrs)
            UnityEngine.Debug.Log($"\t{InstructionToString(instruction)}");

        static string InstructionToString(Instruction instruction) => $"{instruction.Offset:X4} {instruction.OpCode} {InstructionOperandToString(instruction.Operand)}";

        static string InstructionOperandToString(object operand)
        {
            if (operand is ILLabel label)
            {
                return $"(label→ {InstructionToString(label.Target)})";
            }
            else
            {
                return operand?.ToString() ?? "null";
            }
        }
    }
}

[HasteSetting]
public class OneHitKnockOutEnabledSetting : OffOnSetting, IExposedSetting, IConditionalSetting
{
    protected override OffOnMode GetDefaultValue() => OffOnMode.OFF;

    public override List<LocalizedString> GetLocalizedChoices() => [
        // yoink
        new("Settings", "DisabledGraphicOption"),
        new("Settings", "EnabledGraphicOption")
    ];

    public override void ApplyValue() { /* no-op */
        }

    public LocalizedString GetDisplayName() => new UnlocalizedString("One-Hit KO");

    public string GetCategory() => SettingCategory.Difficulty;

    public bool CanShow() => GameDifficulty.CanChangeSettings();
}

public enum OneHitKnockoutOnHit
{
    END_RUN,
    RESTART_NEW_SHARD,
    RESTART_SAME_SHARD
}

[HasteSetting]
public class OneHitKnockOutOnHitSetting : EnumSetting<OneHitKnockoutOnHit>, IExposedSetting
{
    protected override OneHitKnockoutOnHit GetDefaultValue() => OneHitKnockoutOnHit.END_RUN;

    public override List<LocalizedString> GetLocalizedChoices() => [
        new UnlocalizedString("End run"),
        new UnlocalizedString("Restart run on a new Shard"),
        new UnlocalizedString("Restart run in the same Shard"),
    ];

    public override void ApplyValue() { /* no-op */ }

    public LocalizedString GetDisplayName() => new UnlocalizedString("One-Hit KO: On Get Hit");

    public string GetCategory() => SettingCategory.Difficulty;

    public bool CanShow() => GameDifficulty.CanChangeSettings();
}
