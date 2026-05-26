using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using FluxCad48.Geometry;
using System.Collections.Generic;
using Teigha.Colors;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace FluxCad48.ShapeViewAnalysis
{
	public static class ViewIslandDebugDrawer
	{
		private const string DebugLayerName = "FLUX_VIEW_ISLAND_DEBUG";

		public static void Draw(Database db, Editor ed, IEnumerable<ViewIsland> islands)
		{
			if (db == null || islands == null)
				return;

			int drawCount = 0;

			using (Transaction tr = db.TransactionManager.StartTransaction())
			{
				ObjectId layerId = EnsureDebugLayer(db, tr);

				BlockTableRecord space =
					(BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

				int index = 0;

				foreach (ViewIsland island in islands)
				{
					if (island != null && island.Bounds != null)
					{
						DrawIslandBounds(space, tr, island, index, layerId);
						DrawIslandLabel(space, tr, island, index, layerId);
						drawCount++;
					}

					index++;
				}

				tr.Commit();
			}

			if (ed != null)
			{
				ed.WriteMessage(
					"\n[ViewIslandDebugDrawer] Drawn=" + drawCount);

				ed.Regen();
			}
		}

		private static ObjectId EnsureDebugLayer(Database db, Transaction tr)
		{
			LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

			if (lt.Has(DebugLayerName))
				return lt[DebugLayerName];

			lt.UpgradeOpen();

			LayerTableRecord layer = new LayerTableRecord();
			layer.Name = DebugLayerName;
			layer.Color = Color.FromColorIndex(ColorMethod.ByAci, 3);
			layer.IsOff = false;
			layer.IsFrozen = false;
			layer.IsLocked = false;

			ObjectId id = lt.Add(layer);
			tr.AddNewlyCreatedDBObject(layer, true);

			return id;
		}

		private static void DrawIslandBounds(
			BlockTableRecord space,
			Transaction tr,
			ViewIsland island,
			int index,
			ObjectId layerId)
		{
			Bounds2D b = island.Bounds;

			Polyline pl = new Polyline();
			pl.SetDatabaseDefaults();

			pl.LayerId = layerId;
			pl.Color = ResolveColor(island.Role);
			pl.LineWeight = LineWeight.LineWeight070;

			pl.AddVertexAt(0, new Point2d(b.MinX, b.MinY), 0, 0, 0);
			pl.AddVertexAt(1, new Point2d(b.MaxX, b.MinY), 0, 0, 0);
			pl.AddVertexAt(2, new Point2d(b.MaxX, b.MaxY), 0, 0, 0);
			pl.AddVertexAt(3, new Point2d(b.MinX, b.MaxY), 0, 0, 0);
			pl.Closed = true;

			space.AppendEntity(pl);
			tr.AddNewlyCreatedDBObject(pl, true);
		}

		private static void DrawIslandLabel(
			BlockTableRecord space,
			Transaction tr,
			ViewIsland island,
			int index,
			ObjectId layerId)
		{
			Bounds2D b = island.Bounds;

			DBText text = new DBText();
			text.SetDatabaseDefaults();

			text.LayerId = layerId;
			text.Color = ResolveColor(island.Role);
			text.Position = new Point3d(b.MinX, b.MaxY + GetLabelOffset(b), 0);
			text.Height = GetTextHeight(b);

			text.TextString = !string.IsNullOrEmpty(island.DebugLabel)
				? island.DebugLabel
				: "Island " + index + " / " + island.Role;

			space.AppendEntity(text);
			tr.AddNewlyCreatedDBObject(text, true);
		}

		private static Color ResolveColor(ViewIslandRole role)
		{
			switch (role)
			{
				case ViewIslandRole.ShapeView:
					return Color.FromColorIndex(ColorMethod.ByAci, 3); // Green

				case ViewIslandRole.ThinReference:
					return Color.FromColorIndex(ColorMethod.ByAci, 1); // Red

				case ViewIslandRole.DimensionGroup:
					return Color.FromColorIndex(ColorMethod.ByAci, 2); // Yellow

				case ViewIslandRole.TextGroup:
					return Color.FromColorIndex(ColorMethod.ByAci, 4); // Cyan

				case ViewIslandRole.Noise:
					return Color.FromColorIndex(ColorMethod.ByAci, 8); // Gray

				default:
					return Color.FromColorIndex(ColorMethod.ByAci, 7); // White
			}
		}

		private static double GetTextHeight(Bounds2D b)
		{
			double value = b.Height * 0.035;

			if (value < 20)
				value = 20;

			if (value > 80)
				value = 80;

			return value;
		}

		private static double GetLabelOffset(Bounds2D b)
		{
			double value = b.Height * 0.03;

			if (value < 10)
				value = 10;

			if (value > 50)
				value = 50;

			return value;
		}
	}
}