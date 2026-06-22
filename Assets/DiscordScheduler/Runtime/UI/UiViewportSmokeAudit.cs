using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace DiscordScheduler
{
    public sealed class UiSmokeViewportSpec
    {
        public UiSmokeViewportSpec(int width, int height)
        {
            this.width = width;
            this.height = height;
        }

        public int width;
        public int height;

        public string Label => width + "x" + height;
    }

    public sealed class UiSmokeViewResult
    {
        public string viewport = "";
        public string viewName = "";
        public bool ok;
        public string error = "";
    }

    public sealed class UiSmokeReport
    {
        public bool ok = true;
        public List<UiSmokeViewResult> results = new List<UiSmokeViewResult>();

        public List<string> Failures()
        {
            var failures = new List<string>();
            for (int i = 0; i < results.Count; i++)
            {
                if (!results[i].ok)
                    failures.Add(results[i].viewport + " " + results[i].viewName + ": " + results[i].error);
            }

            return failures;
        }
    }

    public sealed class UiViewportSmokeAudit
    {
        public static readonly List<UiSmokeViewportSpec> DefaultViewports = new List<UiSmokeViewportSpec>
        {
            new UiSmokeViewportSpec(1280, 720),
            new UiSmokeViewportSpec(1920, 1080)
        };

        public UiSmokeReport Run(Func<VisualElement> rootFactory, IEnumerable<UiSmokeViewportSpec> viewports = null, IEnumerable<string> views = null)
        {
            var report = new UiSmokeReport();
            if (rootFactory == null)
            {
                report.ok = false;
                report.results.Add(new UiSmokeViewResult { ok = false, error = "Root factory is missing." });
                return report;
            }

            var viewportList = new List<UiSmokeViewportSpec>(viewports ?? DefaultViewports);
            var viewList = new List<string>(views ?? UiBindingManifest.ViewNames);

            for (int i = 0; i < viewportList.Count; i++)
            {
                for (int j = 0; j < viewList.Count; j++)
                {
                    var result = RunOne(rootFactory, viewportList[i], viewList[j]);
                    report.results.Add(result);
                    if (!result.ok)
                        report.ok = false;
                }
            }

            return report;
        }

        private static UiSmokeViewResult RunOne(Func<VisualElement> rootFactory, UiSmokeViewportSpec viewport, string viewName)
        {
            var result = new UiSmokeViewResult
            {
                viewport = viewport?.Label ?? "",
                viewName = viewName ?? ""
            };

            var root = rootFactory();
            if (root == null)
            {
                result.error = "Root visual element is missing.";
                return result;
            }

            root.style.width = viewport?.width ?? 0;
            root.style.height = viewport?.height ?? 0;
            root.style.flexGrow = 0;

            for (int i = 0; i < UiBindingManifest.ViewNames.Count; i++)
            {
                var view = root.Q<VisualElement>(UiBindingManifest.ViewNames[i]);
                if (view != null)
                    view.style.display = string.Equals(view.name, viewName, StringComparison.Ordinal) ? DisplayStyle.Flex : DisplayStyle.None;
            }

            var selectedView = root.Q<VisualElement>(viewName);
            if (selectedView == null)
            {
                result.error = "View is missing.";
                return result;
            }

            var audit = new UiBindingAudit().Run(root, includeNavigationSmoke: true);
            if (!audit.ok)
            {
                result.error = string.Join("; ", audit.BlockingSummaries());
                return result;
            }

            result.ok = true;
            return result;
        }
    }
}
