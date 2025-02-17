﻿using Celeste;
using Celeste.Mod;
using Microsoft.Xna.Framework.Graphics;
using Mono.Cecil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;

namespace SkinModHelper.Module
{
    public class SkinModHelperModule : EverestModule
    {
        public static SkinModHelperModule Instance;

        public override Type SettingsType => typeof(SkinModHelperSettings);
        public static SkinModHelperSettings Settings => (SkinModHelperSettings)Instance._Settings;

        public static Dictionary<string, SkinModHelperConfig> skinConfigs;

        private static ILHook TextboxRunRoutineHook;
        private static List<string> spritesWithHair = new List<string>() 
        { 
            "player", "player_no_backpack", "badeline", "player_badeline", "player_playback" 
        };

        public SkinModHelperModule()
        {
            Instance = this;
            skinConfigs = new Dictionary<string, SkinModHelperConfig>();
        }

        public override void Load()
        {
            Logger.SetLogLevel("SkinModHelper/SkinModHelperModule", LogLevel.Warn);
            Logger.Log("SkinModHelper/SkinModHelperModule", "Initializing SkinModHelper");

            On.Monocle.SpriteBank.Create += SpriteBankCreateHook;
            On.Monocle.SpriteBank.CreateOn += SpriteBankCreateOnHook;
            On.Celeste.LevelLoader.LoadingThread += LevelLoaderLoadingThreadHook;
            On.Celeste.Player.Render += PlayerRenderHook;

            On.Celeste.PlayerHair.GetHairTexture += PlayerHairGetHairTextureHook;
            IL.Celeste.CS06_Campfire.Question.ctor += CampfireQuestionHook;
            TextboxRunRoutineHook = new ILHook(
                typeof(Textbox).GetMethod("RunRoutine", BindingFlags.NonPublic | BindingFlags.Instance).GetStateMachineTarget(),
                SwapTextboxHook);
        }

        public override void LoadContent(bool firstLoad)
        {
            base.LoadContent(firstLoad);
            InitializeSettings();
        }

        public override void Unload()
        {
            Logger.Log("SkinModHelper/SkinModHelperModule", "Unloading SkinModHelper");

            On.Monocle.SpriteBank.Create -= SpriteBankCreateHook;
            On.Monocle.SpriteBank.CreateOn -= SpriteBankCreateOnHook;
            On.Celeste.LevelLoader.LoadingThread -= LevelLoaderLoadingThreadHook;
            On.Celeste.Player.Render -= PlayerRenderHook;

            On.Celeste.PlayerHair.GetHairTexture -= PlayerHairGetHairTextureHook;
            IL.Celeste.CS06_Campfire.Question.ctor -= CampfireQuestionHook;
            TextboxRunRoutineHook.Dispose();
        }
        private void InitializeSettings()
        {
            foreach (ModContent mod in Everest.Content.Mods)
            {
                SkinModHelperConfig config = null;
                if (mod.Map.TryGetValue("SkinModHelperConfig", out ModAsset configAsset) && configAsset.Type == typeof(AssetTypeYaml))
                {
                    config = LoadConfigFile(configAsset);
                    Regex r = new Regex(@"^[a-zA-Z0-9]+_[a-zA-Z0-9]+$");
                    if (string.IsNullOrEmpty(config.SkinId) || !r.IsMatch(config.SkinId) || skinConfigs.ContainsKey(config.SkinId))
                    {
                        Logger.Log("SkinModHelper/SkinModHelperModule", $"Duplicate or invalid skin mod ID {config.SkinId}, will not register.");
                        continue;
                    }
                    if (string.IsNullOrEmpty(config.SkinDialogKey))
                    {
                        Logger.Log("SkinModHelper/SkinModHelperModule", $"Missing or invalid dialog key {config.SkinDialogKey}, will not register.");
                        continue;
                    }
                    skinConfigs.Add(config.SkinId, config);
                    Logger.Log("SkinModHelper/SkinModHelperModule", $"Registered new skin mod: {config.SkinId}");
                }
            }
            if (Settings.SelectedSkinMod == null || !skinConfigs.ContainsKey(Settings.SelectedSkinMod))
            {
                Settings.SelectedSkinMod = SkinModHelperConfig.DEFAULT_SKIN;
            }
        }

        private void PlayerRenderHook(On.Celeste.Player.orig_Render orig, Player self)
        {
            if (Settings.SelectedSkinMod != SkinModHelperConfig.DEFAULT_SKIN)
            {
                int dashCount = self.Dashes;
                string colorGradePath = skinConfigs[Settings.SelectedSkinMod].GetUniquePath() + "dash";

                while (dashCount > 2 && !GFX.ColorGrades.Has(colorGradePath + dashCount))
                {
                    dashCount--;
                }
                if (GFX.ColorGrades.Has(colorGradePath + dashCount))
                {
                    Effect fxColorGrading = GFX.FxColorGrading;
                    fxColorGrading.CurrentTechnique = fxColorGrading.Techniques["ColorGradeSingle"];
                    Engine.Graphics.GraphicsDevice.Textures[1] = GFX.ColorGrades[colorGradePath + dashCount].Texture.Texture_Safe;
                    Draw.SpriteBatch.End();
                    Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointWrap, DepthStencilState.None, RasterizerState.CullNone, fxColorGrading, (self.Scene as Level).GameplayRenderer.Camera.Matrix);
                    orig(self);
                    Draw.SpriteBatch.End();
                    Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointWrap, DepthStencilState.None, RasterizerState.CullNone, null, (self.Scene as Level).GameplayRenderer.Camera.Matrix);
                    return;
                }
            }
            orig(self);
        }

        private MTexture PlayerHairGetHairTextureHook(On.Celeste.PlayerHair.orig_GetHairTexture orig, PlayerHair self, int index)
        {
            if (Settings.SelectedSkinMod != SkinModHelperConfig.DEFAULT_SKIN)
            {
                if (index == 0)
                {
                    string newBangsPath = skinConfigs[Settings.SelectedSkinMod].GetUniquePath() + "characters/player/bangs";
                    if (GFX.Game.Has(newBangsPath + "00"))
                    {
                        return GFX.Game.GetAtlasSubtextures(newBangsPath)[self.Sprite.HairFrame];
                    }
                }
                string newHairPath = skinConfigs[Settings.SelectedSkinMod].GetUniquePath() + "characters/player/hair00";
                if (GFX.Game.Has(newHairPath))
                {
                    return GFX.Game[newHairPath];
                }
            }
            return orig(self, index);
        }

        // If our current skinmod has an overridden sprite bank, use that sprite data instead
        private Sprite SpriteBankCreateOnHook(On.Monocle.SpriteBank.orig_CreateOn orig, SpriteBank self, Sprite sprite, string id)
        {
            String newId = id + "_" + Settings.SelectedSkinMod;
            if (self.SpriteData.ContainsKey(newId))
            {
                id = newId;
            }
            return orig(self, sprite, id);
        }

        private Sprite SpriteBankCreateHook(On.Monocle.SpriteBank.orig_Create orig, SpriteBank self, string id)
        {
            String newId = id + "_" + Settings.SelectedSkinMod;
            if (self.SpriteData.ContainsKey(newId))
            {
                id = newId;
            }
            return orig(self, id);
        }

        // Wait until the main sprite bank is created, then combine with our skin mod banks
        private void LevelLoaderLoadingThreadHook(On.Celeste.LevelLoader.orig_LoadingThread orig, LevelLoader self)
        {
            foreach (KeyValuePair<string, SkinModHelperConfig> config in skinConfigs)
            {
                string skinId = config.Key;
                if (skinId == SkinModHelperConfig.DEFAULT_SKIN)
                {
                    continue;
                }
                string spritesXmlPath = "Graphics/" + config.Value.GetUniquePath() + "Sprites.xml";
                string portraitsXmlPath = "Graphics/" + config.Value.GetUniquePath() + "Portraits.xml";
                CombineSpriteBanks(GFX.SpriteBank, skinId, spritesXmlPath);
                CombineSpriteBanks(GFX.PortraitsSpriteBank, skinId, portraitsXmlPath);
            }
            orig(self);
        }

        private void SwapTextboxHook(ILContext il)
        {
            ILCursor cursor = new ILCursor(il);
            // Move to the last occurence of this
            while (cursor.TryGotoNext(MoveType.After, instr => instr.MatchIsinst<FancyText.Portrait>())) { }
            // Make sure nothing went wrong
            if (cursor.Prev?.MatchIsinst<FancyText.Portrait>() == true)
            {
                Logger.Log("SkinModHelper/SkinModHelperModule", $"Changing portrait path at {cursor.Index} in CIL code for {cursor.Method.FullName}");
                cursor.EmitDelegate<Func<FancyText.Portrait, FancyText.Portrait>>(ReplacePortraitPath);
            }
        }

        // This one requires double hook - for some reason they implemented a tiny version of the Textbox class that behaves differently
        private void CampfireQuestionHook(ILContext il)
        {
            ILCursor cursor = new ILCursor(il);
            // Move to the last occurence of this
            while (cursor.TryGotoNext(MoveType.After, instr => instr.MatchIsinst<FancyText.Portrait>())) { }
            // Make sure nothing went wrong
            if (cursor.Prev?.MatchIsinst<FancyText.Portrait>() == true)
            {
                Logger.Log("SkinModHelper/SkinModHelperModule", $"Changing portrait path at {cursor.Index} in CIL code for {cursor.Method.FullName}");
                cursor.EmitDelegate<Func<FancyText.Portrait, FancyText.Portrait>>(ReplacePortraitPath);
            }

            if (cursor.TryGotoNext(MoveType.After, instr => instr.MatchLdstr("_ask"), 
                instr => instr.MatchCall(out MethodReference method) && method.Name == "Concat"))
            {
                Logger.Log("SkinModHelper/SkinModHelperModule", $"Changing textbox path at {cursor.Index} in CIL code for {cursor.Method.FullName}");
                cursor.EmitDelegate<Func<string, string>>(ReplaceTextboxPath);
            }
        }

        private static FancyText.Portrait ReplacePortraitPath(FancyText.Portrait portrait)
        {
            if (Settings.SelectedSkinMod != SkinModHelperConfig.DEFAULT_SKIN)
            {
                string skinModPortraitSpriteId = portrait.SpriteId + "_" + Settings.SelectedSkinMod;
                if (GFX.PortraitsSpriteBank.Has(skinModPortraitSpriteId))
                {
                    portrait.Sprite = skinModPortraitSpriteId.Replace("portrait_", "");
                }
            }
            return portrait;
        }

        // ReplacePortraitPath makes textbox path funky, so correct to our real path or revert to vanilla if it does not exist
        private static string ReplaceTextboxPath(string textboxPath)
        {
            if (Settings.SelectedSkinMod != SkinModHelperConfig.DEFAULT_SKIN)
            {
                string originalPortraitId = textboxPath.Split('_')[0].Replace("textbox/", ""); // "textbox/[orig portrait id]_[skin id]_ask"
                string newTextboxPath = "textbox/" + skinConfigs[Settings.SelectedSkinMod].GetUniquePath() + originalPortraitId + "_ask";
                if (GFX.Portraits.Has(newTextboxPath))
                {
                    textboxPath = newTextboxPath;
                }
            }
            return textboxPath;
        }

        private static SkinModHelperConfig LoadConfigFile(ModAsset skinConfigYaml)
        {
            return skinConfigYaml.Deserialize<SkinModHelperConfig>();
        }

        // Trigger when we change the setting, store the new one. If in-level, redraw player sprite.
        public static void UpdateSkin(string skinId)
        {
            Settings.SelectedSkinMod = skinId;
            Player player = (Engine.Scene)?.Tracker.GetEntity<Player>();
            if (player != null)
            {
                if (player.Active)
                {
                    player.ResetSpriteNextFrame(player.Sprite.Mode);
                }
                else
                {
                    player.ResetSprite(player.Sprite.Mode);
                }
            }
        }

        // Combine skin mod XML with a vanilla sprite bank
        private void CombineSpriteBanks(SpriteBank origBank, string skinId, string xmlPath)
        {
            SpriteBank newBank = BuildBank(origBank, xmlPath);
            if (newBank == null)
            {
                return;
            }

            // For each overridden sprite, patch it and add it to the original bank with a unique identifier
            foreach (KeyValuePair<string, SpriteData> spriteDataEntry in newBank.SpriteData)
            {
                string spriteId = spriteDataEntry.Key;
                SpriteData newSpriteData = spriteDataEntry.Value;

                if (origBank.SpriteData.TryGetValue(spriteId, out SpriteData origSpriteData))
                {
                    PatchSprite(origSpriteData.Sprite, newSpriteData.Sprite);

                    string newSpriteId = spriteId + "_" + skinId;
                    origBank.SpriteData[newSpriteId] = newSpriteData;

                    // Build hair!
                    if (spritesWithHair.Contains(spriteId))
                    {
                        PlayerSprite.CreateFramesMetadata(newSpriteId);
                    }
                }
            }
        }

        private SpriteBank BuildBank (SpriteBank origBank, string xmlPath)
        {
            try
            {
                SpriteBank newBank = new SpriteBank(origBank.Atlas, xmlPath);
                Logger.Log("SkinModHelper/SkinModHelperModule", $"Built sprite bank for {xmlPath}.");
                return newBank;
            }
            catch (Exception e)
            {
                Logger.Log("SkinModHelper/SkinModHelperModule", $"Could not build sprite bank for {xmlPath}: {e.Message}.");
                return null;
            }
        }

        // Add any missing vanilla animations to an overridden sprite
        private void PatchSprite(Sprite origSprite, Sprite newSprite)
        {
            Dictionary<string, Sprite.Animation> newAnims = newSprite.GetAnimations();
            foreach (KeyValuePair<string, Sprite.Animation> animEntry in origSprite.GetAnimations())
            {
                string origAnimId = animEntry.Key;
                Sprite.Animation origAnim = animEntry.Value;
                if (!newAnims.ContainsKey(origAnimId))
                {
                    newAnims[origAnimId] = origAnim;
                }
            }
        }
    }
}
