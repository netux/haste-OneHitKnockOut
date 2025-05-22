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
        get {
            var setting = GameHandler.Instance?.SettingsHandler?.GetSetting<OneHitKnockOutEnabledSetting>();
            if (setting == null)
            {
                return false;
            }

            return setting.Value == OffOnMode.ON;
        }
    }

    public static OneHitKnockoutOnHit OnHit
    {
        get => GameHandler.Instance.SettingsHandler.GetSetting<OneHitKnockOutOnHitSetting>().Value;
    }

    static OneHitKnockOut()
    {
        // TODO(netux): show a counter of how many automatic restarts there were

        #region Main logic hooks
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

            if (!RunHandler.InRun)
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
                case OneHitKnockoutOnHit.RESTART_SAME_SEED:
                case OneHitKnockoutOnHit.RESTART_NEW_SEED:
                    var config = RunHandler.config;
                    var shardID = RunHandler.RunData.shardID;
                    var seed = OnHit == OneHitKnockoutOnHit.RESTART_SAME_SEED
                        ? RunHandler.RunData.currentSeed
                        : RunHandler.GenerateSeed();

                    RunHandler.ClearCurrentRun();
                    RunHandler.StartAndPlayNewRun(config, shardID, seed);
                    break;
            }
        };
        #endregion Main logic hooks

        #region Update HUD hooks
        On.UI_HealthBar.Update += (original, healthBar) =>
        {
            if (!IsOHKOEnabled)
            {
                original(healthBar);
                return;
            }

            healthBar.bar.SetValues(
                /* fills: */ Player.localPlayer.HealthPercentage() < 1f ? 0f : 1f,
                /* segments: */ 1f,
                /* activationAmount: */ Player.localPlayer.data.HealingFeedback()
            );
        };

        IL.UI_PlayerLives.Update += (il) =>
        {
            var cursor = new ILCursor(il);

            // if (HasteSpectate.TryGet(out data)) {
            if (!cursor.TryGotoNext(
                MoveType.After,
                i => i.MatchCallOrCallvirt(typeof(HasteSpectate).GetMethod(nameof(HasteSpectate.TryGet), BindingFlags.Public | BindingFlags.Static)),
                i => i.MatchBrfalse(out _)
            ))
            {
                UnityEngine.Debug.LogError("Could not find call to HasteSpectate.TryGet() in UI_PlayerLives.Update(). Hearts UI may not immediately change when enabling OHKO");
                return;
            }

            var afterOHKOHandlingLabel = cursor.DefineLabel();

            //   if (OneHitKnockOut.IsOHKOEnabled) {
            cursor.Emit(OpCodes.Call, typeof(OneHitKnockOut).GetMethod($"get_{nameof(IsOHKOEnabled)}", BindingFlags.Static | BindingFlags.Public));
            cursor.Emit(OpCodes.Brfalse_S, afterOHKOHandlingLabel);

            cursor.Emit(OpCodes.Ldarg_0); // this
            cursor.EmitDelegate((UI_PlayerLives uiPlayerLives) =>
            {
                var heartsToBeEnabled = isTriggeringOHKOPayloadAlready ? 0 : 1;

                if (uiPlayerLives.hearts.Count != 1)
                {
                    uiPlayerLives.MaxLivesChanged(/* lives: */ heartsToBeEnabled, /* maxLives: */ 1);
                    return;
                }

                if (uiPlayerLives.heartsEnabled != heartsToBeEnabled)
                {
                    uiPlayerLives.LivesChanged(/* lives: */ heartsToBeEnabled);
                }
            });

            //     return;
            cursor.Emit(OpCodes.Ret);
            //   }

            cursor.MarkLabel(afterOHKOHandlingLabel);

            //   ... original code ...
            // }
        };
        #endregion Update HUD hooks
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
    RESTART_NEW_SEED,
    RESTART_SAME_SEED
}

[HasteSetting]
public class OneHitKnockOutOnHitSetting : EnumSetting<OneHitKnockoutOnHit>, IExposedSetting
{
    protected override OneHitKnockoutOnHit GetDefaultValue() => OneHitKnockoutOnHit.END_RUN;

    public override List<LocalizedString> GetLocalizedChoices() => [
        new UnlocalizedString("End run"),
        new UnlocalizedString("Restart run with a new seed"),
        new UnlocalizedString("Restart run with the same seed"),
    ];

    public override void ApplyValue() { /* no-op */ }

    public LocalizedString GetDisplayName() => new UnlocalizedString("One-Hit KO: On Get Hit");

    public string GetCategory() => SettingCategory.Difficulty;

    public bool CanShow() => GameDifficulty.CanChangeSettings();
}
