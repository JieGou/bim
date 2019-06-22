﻿using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Tracer = System.Diagnostics.Trace;

namespace TektaRevitPlugins
{
    [Autodesk.Revit.Attributes.Transaction
        (Autodesk.Revit.Attributes.TransactionMode.Manual)]
    [Autodesk.Revit.Attributes.Journaling
        (Autodesk.Revit.Attributes.JournalingMode.NoCommandData)]
    class WallJoinerCmd : Autodesk.Revit.UI.IExternalCommand
    {
        public Autodesk.Revit.UI.Result Execute(Autodesk.Revit.UI.
            ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Tracer.Listeners.Add(new System.Diagnostics.EventLogTraceListener("Application"));

            Autodesk.Revit.UI.UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try {

                // Set the minimum wall width
                double minWallWidth = UnitUtils
                    .ConvertToInternalUnits(400, DisplayUnitType.DUT_MILLIMETERS); // 400mm

                // Select all walls wider than 400 mm
                ElementParameterFilter widthFilter =
                    new ElementParameterFilter(ParameterFilterRuleFactory
                    .CreateGreaterOrEqualRule(new ElementId(BuiltInParameter.WALL_ATTR_WIDTH_PARAM),
                    minWallWidth, 0.1));

                IList<Wall> walls = new FilteredElementCollector(doc)
                    .OfClass(typeof(Wall))
                    .WhereElementIsNotElementType()
                    .WherePasses(widthFilter)
                    .Cast<Wall>()
                    .ToList();

                Tracer.Write("The number of walls in the project: " + walls.Count);

                using (Transaction t = new Transaction(doc, "Join walls")) {
                    FailureHandlingOptions handlingOptions = t.GetFailureHandlingOptions();
                    handlingOptions.SetFailuresPreprocessor(new AllWarningSwallower());
                    t.SetFailureHandlingOptions(handlingOptions);
                    t.Start();

                    foreach (Wall wall in walls) {
                        IList<Element> intrudingWalls =
                            GetWallsIntersectBoundingBox(doc, wall, minWallWidth);

                        Tracer.Write(string.Format("Wall {0} is intersected by {1} walls",
                            wall.Id.ToString(), intrudingWalls.Count));

                        foreach (Element elem in intrudingWalls) {
                            if (//((Wall)elem).WallType.Width < minWallWidth ||
                                ((Wall)elem).WallType.Kind == WallKind.Curtain)
                                continue;
                            try {
                                if (!JoinGeometryUtils.AreElementsJoined(doc, wall, elem))
                                    JoinGeometryUtils.JoinGeometry(doc, wall, elem);
                            }
                            catch (Exception ex) {
                                Tracer.Write(string.Format("{0}\nWall: {1} cannot be joined to {2}",
                                    ex.Message, wall.Id.ToString(), elem.Id.ToString()));
                                continue;
                            }
                        }
                    }
                    t.Commit();
                }

                return Autodesk.Revit.UI.Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) {
                return Autodesk.Revit.UI.Result.Cancelled;
            }
            catch (Exception ex) {
                Tracer.Write(string.Format("{0}\n{1}",
                    ex.Message, ex.StackTrace));
                return Autodesk.Revit.UI.Result.Failed;
            }
        }

        public static IList<Element> GetWallsIntersectBoundingBox(Document doc, Element elem, double minWallWidth)
        {
            ExclusionFilter exclusionFilter =
                    new ExclusionFilter(new List<ElementId> { elem.Id });

            ElementParameterFilter elementParameterFilter =
                new ElementParameterFilter(ParameterFilterRuleFactory
                .CreateEqualsRule(new ElementId(BuiltInParameter.WALL_BASE_CONSTRAINT), elem.LevelId), true);

            BoundingBoxXYZ bndBx = elem.get_Geometry
                (new Options { IncludeNonVisibleObjects = true }).GetBoundingBox();

            BoundingBoxIntersectsFilter bndBxFlt =
                new BoundingBoxIntersectsFilter(new Outline(bndBx.Min, bndBx.Max), 0.1);

            ElementParameterFilter widthFilter =
                new ElementParameterFilter(ParameterFilterRuleFactory
                .CreateGreaterOrEqualRule(new ElementId(BuiltInParameter.WALL_ATTR_WIDTH_PARAM),
                minWallWidth, 0.1));

            IList<Element> elements = new FilteredElementCollector(doc)
                    .WherePasses(exclusionFilter)
                    .OfClass(typeof(Wall))
                    .WherePasses(bndBxFlt)
                    .WherePasses(widthFilter)
                    .WherePasses(elementParameterFilter)
                    .ToElements()
                    .ToList();

            return elements;
        }
    }

    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    class BoundingBoxIntesector : Autodesk.Revit.UI.IExternalCommand
    {
        public Autodesk.Revit.UI.Result Execute
            (Autodesk.Revit.UI.ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            // Get access to the active uidoc and doc
            Autodesk.Revit.UI.UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = commandData.Application.ActiveUIDocument.Document;
            Autodesk.Revit.UI.Selection.Selection sel = uidoc.Selection;

            try {

                Reference r =
                    sel.PickObject(Autodesk.Revit.UI.Selection.ObjectType.Element);
                Element elem = doc.GetElement(r);


                IList<ElementId> elemsWithinBBox = WallJoinerCmd
                    .GetWallsIntersectBoundingBox(doc, elem, UnitUtils
                    .ConvertToInternalUnits(400, DisplayUnitType.DUT_MILLIMETERS))
                    .Select(e => e.Id)
                    .ToList();

                sel.SetElementIds(elemsWithinBBox);

                return Autodesk.Revit.UI.Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) {
                return Autodesk.Revit.UI.Result.Cancelled;
            }
            catch (Exception ex) {
                Autodesk.Revit.UI.TaskDialog.Show("Exception",
                    string.Format("Message: {0}\nStackTrace: {1}",
                    ex.Message, ex.StackTrace));
                return Autodesk.Revit.UI.Result.Failed;
            }
        }
    }
}
