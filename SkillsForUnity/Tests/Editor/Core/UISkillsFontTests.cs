using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace UnitySkills.Tests.Core
{
    [TestFixture]
    public class UISkillsFontTests
    {
        [Test]
        public void FontAsset_IsStaticAndAllRenderResourcesArePersistent()
        {
            var fontAsset = AssetDatabase.LoadAssetAtPath<FontAsset>(UISkillsFont.FontAssetPath);

            Assert.That(fontAsset, Is.Not.Null);
            Assert.That(fontAsset.atlasPopulationMode, Is.EqualTo(AtlasPopulationMode.Static));
            Assert.That(UISkillsFont.IsPersistentAndComplete(fontAsset), Is.True);
            Assert.That(AssetDatabase.GetAssetPath(fontAsset.material),
                Is.EqualTo(UISkillsFont.FontAssetPath));
            Assert.That(AssetDatabase.GetAssetPath(fontAsset.atlasTextures[0]),
                Is.EqualTo(UISkillsFont.FontAssetPath));
        }

        [Test]
        public void CustomFont_ContainsEveryFixedUiCharacter()
        {
            // 验的是**源 Font**而不是烘焙出的 FontAsset：面板绑定走
            // unityFontDefinition = FontDefinition.FromFont(源 Font)，真正光栅化字形的是它。
            // 烘焙产物的完整性由本文件的 FontAsset_IsStaticAndAllRenderResourcesArePersistent 覆盖，
            // 这里再验一遍是重复的，而且验的不是实际渲染路径。
            var font = AssetDatabase.LoadAssetAtPath<Font>(UISkillsFont.TtfPath);
            Assert.That(font, Is.Not.Null);

            var characters = UISkillsFontAssetBaker.CollectUiCharacters();
            var missing = characters
                .Where(value => !font.HasCharacter(value))
                .Distinct()
                .ToArray();

            Assert.That(missing, Is.Empty,
                "Missing fixed UI characters: " + string.Join(" ",
                    missing.Select(value => $"{value} (U+{(int)value:X4})")));
        }

        /// <summary>
        /// Every character in the atlas must exclusively own one glyph.
        ///
        /// HasCharacter can't see this class of failure: if incremental glyph top-up appends a character record but reuses a glyph index,
        /// every HasCharacter check still passes, yet the panel will render one character's glyph shape at another character's position —
        /// affected text renders completely wrong with no error raised, and none of the existing assertions catch it. Two characters sharing one glyph index
        /// is the numeric signature of this failure.
        ///
        /// Assert by count, not by enumerating characters: the atlas grows with every new piece of UI copy, so what we assert is "the bijection itself", not some particular size.
        /// </summary>
        [Test]
        public void FontAsset_MapsEveryCharacterToItsOwnGlyph()
        {
            var fontAsset = AssetDatabase.LoadAssetAtPath<FontAsset>(UISkillsFont.FontAssetPath);
            Assert.That(fontAsset, Is.Not.Null);

            var characterTable = fontAsset.characterTable;
            Assert.That(characterTable, Is.Not.Null.And.Not.Empty, "Character table is empty.");

            var duplicated = characterTable
                .GroupBy(character => character.glyphIndex)
                .Where(group => group.Count() > 1)
                .ToArray();

            Assert.That(duplicated, Is.Empty,
                $"{duplicated.Length} glyph index/indices are shared by more than one character, " +
                "so those characters render each other's shapes. Offenders: " +
                string.Join("; ", duplicated.Take(10).Select(group =>
                    $"glyph {group.Key} <- " + string.Join(", ",
                        group.Select(character => $"U+{character.unicode:X4}")))));

            Assert.That(characterTable.Select(character => character.glyphIndex).Distinct().Count(),
                Is.EqualTo(characterTable.Count),
                "Character-to-glyph mapping must be a bijection.");
        }

        [Test]
        public void Apply_UsesVersionCompatibleCustomFontAndIsIdempotent()
        {
            var expected = AssetDatabase.LoadAssetAtPath<Font>(UISkillsFont.TtfPath);
            var root = new VisualElement();
            root.style.unityFontDefinition = new StyleFontDefinition(StyleKeyword.Null);

            UISkillsFont.Apply(root);
            UISkillsFont.Apply(root);

#if UNITY_6000_0_OR_NEWER
            Assert.That(root.style.unityFont.keyword, Is.EqualTo(StyleKeyword.Null));
            Assert.That(root.style.unityFontDefinition.value.font, Is.SameAs(expected));
            Assert.That(root.style.unityFontDefinition.value.fontAsset, Is.Null);
#else
            // 2022 binds the dynamic Font directly so glyphs rasterize at target pixel
            // size (crisp on HiDPI); the definition must stay cleared to not override it.
            Assert.That(root.style.unityFontDefinition.keyword, Is.EqualTo(StyleKeyword.Null));
            Assert.That(root.style.unityFont.value, Is.SameAs(expected));
#endif
        }

        [Test]
        public void Apply_WithMissingCustomFont_ClearsStaleFontDefinition()
        {
            var root = new VisualElement();
            UISkillsFont.Apply(root);

            UISkillsFont.Apply(root, (Font)null);

            Assert.That(root.style.unityFont.keyword, Is.EqualTo(StyleKeyword.Null));
            Assert.That(root.style.unityFontDefinition.keyword, Is.EqualTo(StyleKeyword.Null));
            Assert.That(root.style.unityFontDefinition.value.font, Is.Null);
            Assert.That(root.style.unityFontDefinition.value.fontAsset, Is.Null);
        }

        [Test]
        public void Apply_UsesCustomFontRegardlessOfCurrentLanguage()
        {
            var saved = SkillsLocalization.Current;
            try
            {
                foreach (var language in new[]
                         {
                             SkillsLocalization.Language.English,
                             SkillsLocalization.Language.Russian,
                             SkillsLocalization.Language.Chinese
                         })
                {
                    SkillsLocalization.Current = language;
                    var root = new VisualElement();

                    UISkillsFont.Apply(root);

#if UNITY_6000_0_OR_NEWER
                    var expected = AssetDatabase.LoadAssetAtPath<Font>(UISkillsFont.TtfPath);
                    Assert.That(root.style.unityFontDefinition.value.font, Is.SameAs(expected),
                        $"Custom font must be applied for {language}");
#else
                    var expected = AssetDatabase.LoadAssetAtPath<FontAsset>(UISkillsFont.FontAssetPath);
                    Assert.That(root.style.unityFontDefinition.value.fontAsset, Is.SameAs(expected),
                        $"Custom font must be applied for {language}");
#endif
                }
            }
            finally
            {
                SkillsLocalization.Current = saved;
            }
        }

#if !UNITY_6000_0_OR_NEWER
        [Test]
        public void AppliedFont_SurvivesImmediateUnusedAssetCleanup()
        {
            var root = new VisualElement();
            UISkillsFont.Apply(root);

            EditorUtility.UnloadUnusedAssetsImmediate();

            var font = root.style.unityFont.value;
            Assert.That((bool)font, Is.True,
                "Bound dynamic Font must survive an immediate unused-asset sweep");
        }
#endif

        [Test]
        public void Stylesheets_DoNotRequestSyntheticBold()
        {
            var paths = new[]
            {
                "Packages/com.besty.unity-skills/Editor/UI/UnitySkillsWindow.uss",
                "Packages/com.besty.unity-skills/Editor/UI/AuditLogWindow.uss",
                "Packages/com.besty.unity-skills/Editor/UI/AllowlistPickerWindow.uss",
                "Packages/com.besty.unity-skills/Editor/UI/UnityCliWindow.uss",
            };

            foreach (var path in paths)
            {
                Assert.That(File.ReadAllText(path), Does.Not.Contain("-unity-font-style: bold;"),
                    $"All UnitySkills text should use the bundled font's native Regular weight: {path}");
            }
        }
    }
}

// Producer:Betsy
