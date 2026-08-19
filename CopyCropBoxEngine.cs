using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace BCCPlugIn
{
    public class CopyCropBoxEngine
    {
        private readonly Document _doc;

        public CopyCropBoxEngine(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        public int CopyCropBoxToViews(View sourceView, List<View> targetViews)
        {
            if (sourceView == null) throw new ArgumentNullException(nameof(sourceView));
            if (targetViews == null || targetViews.Count == 0) return 0;

            BoundingBoxXYZ sourceCrop = sourceView.CropBox;
            bool cropActive = sourceView.CropBoxActive;
            bool cropVisible = sourceView.CropBoxVisible;

            ViewPlan sourcePlan = sourceView as ViewPlan;
            PlanViewRange sourceRange = sourcePlan != null ? sourcePlan.GetViewRange() : null;

            int count = 0;
            using (Transaction t = new Transaction(_doc, "BIMBCC: Копирование обрезки видов"))
            {
                t.Start();
                foreach (View target in targetViews)
                {
                    if (target.Id == sourceView.Id || target.IsTemplate) continue;

                    try
                    {
                        target.CropBoxActive = cropActive;
                        target.CropBoxVisible = cropVisible;
                        if (sourceCrop != null)
                        {
                            target.CropBox = sourceCrop;
                        }

                        if (sourceRange != null && target is ViewPlan targetPlan)
                        {
                            PlanViewRange targetRange = targetPlan.GetViewRange();
                            targetRange.SetOffset(PlanViewPlane.CutPlane, sourceRange.GetOffset(PlanViewPlane.CutPlane));
                            targetRange.SetOffset(PlanViewPlane.TopClipPlane, sourceRange.GetOffset(PlanViewPlane.TopClipPlane));
                            targetRange.SetOffset(PlanViewPlane.BottomClipPlane, sourceRange.GetOffset(PlanViewPlane.BottomClipPlane));
                            targetRange.SetOffset(PlanViewPlane.ViewDepthPlane, sourceRange.GetOffset(PlanViewPlane.ViewDepthPlane));
                            targetPlan.SetViewRange(targetRange);
                        }

                        count++;
                    }
                    catch { }
                }
                t.Commit();
            }

            return count;
        }
    }
}
