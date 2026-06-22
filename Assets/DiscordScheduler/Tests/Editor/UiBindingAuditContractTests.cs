using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace DiscordScheduler.Tests
{
    public sealed class UiBindingAuditContractTests
    {
        [Test]
        public void MainUxml_SatisfiesRequiredControlManifest()
        {
            var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/DiscordScheduler/Resources/DiscordScheduler/UI/Main.uxml");
            Assert.That(asset, Is.Not.Null);

            var root = asset.CloneTree();
            var report = new UiBindingAudit().Run(root, includeNavigationSmoke: false);

            Assert.That(report.ok, Is.True, string.Join("; ", report.BlockingSummaries()));
        }

        [Test]
        public void MainUxml_DefaultSourceShowsDashboardOnly()
        {
            var uxml = File.ReadAllText("Assets/DiscordScheduler/Resources/DiscordScheduler/UI/Main.uxml");

            Assert.That(
                HasViewDisplayStyle(uxml, "viewDashboard", "flex"),
                Is.True,
                "Dashboard should be visible before runtime scripts bind, otherwise the app can render a blank content area.");

            foreach (var viewName in UiBindingManifest.ViewNames.Where(viewName => viewName != "viewDashboard"))
                Assert.That(HasViewDisplayStyle(uxml, viewName, "none"), Is.True, viewName + " should start hidden.");
        }

        [Test]
        public void MainUxml_ActionControlsHaveUserGuidanceTooltips()
        {
            var document = XDocument.Parse(File.ReadAllText("Assets/DiscordScheduler/Resources/DiscordScheduler/UI/Main.uxml"));

            var buttonsWithoutTooltip = document
                .Descendants()
                .Where(element => element.Name.LocalName == "Button")
                .Where(element => string.IsNullOrWhiteSpace((string)element.Attribute("tooltip")))
                .Select(UserFacingElementName)
                .ToArray();
            Assert.That(buttonsWithoutTooltip, Is.Empty, "Buttons should explain their effect on hover.");

            var dangerButtonsWithoutTooltip = document
                .Descendants()
                .Where(element => element.Name.LocalName == "Button")
                .Where(element => HasClass(element, "dangerButton"))
                .Where(element => string.IsNullOrWhiteSpace((string)element.Attribute("tooltip")))
                .Select(UserFacingElementName)
                .ToArray();
            Assert.That(dangerButtonsWithoutTooltip, Is.Empty, "Destructive actions need explicit hover guidance.");

            var criticalFields = new[]
            {
                "tfTargetWebhook",
                "ddPostTarget",
                "tfPostBody",
                "tfDate",
                "tfTime",
                "btnPayloadPreviewRefresh",
                "btnSchedule",
                "btnReviewRetry",
                "btnReviewDismiss",
                "btnRetentionApply",
                "btnRestoreApply"
            };
            foreach (var controlName in criticalFields)
            {
                var element = FindElementByName(document, controlName);
                Assert.That(element, Is.Not.Null, controlName + " should exist in Main.uxml.");
                Assert.That((string)element.Attribute("tooltip"), Is.Not.Null.And.Not.Empty, controlName + " should explain the user-facing consequence.");
            }
        }

        [Test]
        public void MainUxml_ViewportSmoke_CoversAllViewsAtDesktopSizes()
        {
            var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/DiscordScheduler/Resources/DiscordScheduler/UI/Main.uxml");
            Assert.That(asset, Is.Not.Null);

            var report = new UiViewportSmokeAudit().Run(() => asset.CloneTree());

            Assert.That(report.ok, Is.True, string.Join("; ", report.Failures()));
            Assert.That(report.results.Count, Is.EqualTo(UiBindingManifest.ViewNames.Count * UiViewportSmokeAudit.DefaultViewports.Count));
            Assert.That(report.results.Any(result => result.viewport == "1280x720" && result.viewName == "viewDashboard" && result.ok), Is.True);
            Assert.That(report.results.Any(result => result.viewport == "1920x1080" && result.viewName == "viewSettings" && result.ok), Is.True);
        }

        [Test]
        public void SyntheticRoot_AllRequiredControlsPass()
        {
            var root = BuildSyntheticRoot();

            var report = new UiBindingAudit().Run(root);

            Assert.That(report.ok, Is.True, string.Join("; ", report.BlockingSummaries()));
        }

        [Test]
        public void MissingRequiredControl_FailsWithUserImpact()
        {
            var root = BuildSyntheticRoot("btnSchedule");

            var report = new UiBindingAudit().Run(root);

            Assert.That(report.ok, Is.False);
            Assert.That(report.issues.Any(issue => issue.code == "missing-control" && issue.controlName == "btnSchedule"), Is.True);
            Assert.That(report.issues.First(issue => issue.controlName == "btnSchedule").userImpact, Is.Not.Empty);
        }

        [Test]
        public void DuplicateRequiredName_WarnsButDoesNotBlock()
        {
            var root = BuildSyntheticRoot();
            root.Add(new Button { name = "btnPostSave" });

            var report = new UiBindingAudit().Run(root);

            Assert.That(report.ok, Is.True, string.Join("; ", report.BlockingSummaries()));
            Assert.That(report.issues.Any(issue => issue.code == "duplicate-control-name" && issue.controlName == "btnPostSave"), Is.True);
        }

        [Test]
        public void NavigationSmoke_TwoVisibleViewsFails()
        {
            var root = BuildSyntheticRoot();
            root.Q<VisualElement>("viewTargets").style.display = DisplayStyle.Flex;

            var report = new UiBindingAudit().Run(root);

            Assert.That(report.ok, Is.False);
            Assert.That(report.issues.Any(issue => issue.code == "navigation-visible-count"), Is.True);
        }

        private static VisualElement BuildSyntheticRoot(string omitName = null)
        {
            var root = new VisualElement { name = "Root" };
            foreach (var control in UiBindingManifest.RequiredControls)
            {
                if (string.Equals(control.name, omitName, StringComparison.Ordinal))
                    continue;

                var element = CreateElement(control.controlType);
                element.name = control.name;

                if (UiBindingManifest.ViewNames.Contains(control.name))
                    element.style.display = control.name == "viewDashboard" ? DisplayStyle.Flex : DisplayStyle.None;

                root.Add(element);
            }

            return root;
        }

        private static VisualElement CreateElement(Type type)
        {
            if (type == typeof(Button))
                return new Button();

            return (VisualElement)Activator.CreateInstance(type);
        }

        private static bool HasViewDisplayStyle(string uxml, string viewName, string displayValue)
        {
            var document = XDocument.Parse(uxml);
            var view = FindElementByName(document, viewName);

            return string.Equals(
                GetStyleValue((string)view?.Attribute("style"), "display"),
                displayValue,
                StringComparison.OrdinalIgnoreCase);
        }

        private static XElement FindElementByName(XDocument document, string name)
        {
            return document
                .Descendants()
                .FirstOrDefault(element => string.Equals((string)element.Attribute("name"), name, StringComparison.Ordinal));
        }

        private static bool HasClass(XElement element, string className)
        {
            var classes = ((string)element.Attribute("class") ?? "").Split(' ');
            return classes.Any(item => string.Equals(item.Trim(), className, StringComparison.Ordinal));
        }

        private static string UserFacingElementName(XElement element)
        {
            var name = (string)element.Attribute("name");
            if (!string.IsNullOrWhiteSpace(name))
                return name;

            return element.Name.LocalName + ":" + ((string)element.Attribute("text") ?? "");
        }

        private static string GetStyleValue(string style, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(style))
                return string.Empty;

            foreach (var declaration in style.Split(';'))
            {
                var separator = declaration.IndexOf(':');
                if (separator < 0)
                    continue;

                var name = declaration.Substring(0, separator).Trim();
                if (!string.Equals(name, propertyName, StringComparison.OrdinalIgnoreCase))
                    continue;

                return declaration.Substring(separator + 1).Trim();
            }

            return string.Empty;
        }
    }
}
