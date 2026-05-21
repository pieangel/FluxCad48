using System;
using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;
using FluxCad48.Brics;
using FluxCad48.Geometry;

namespace FluxCad48.Commands
{
	public class FluxDebugSelectedCoordsDeepCommand
	{
		[CommandMethod("FLUX_DEBUG_SELECTED_COORDS_DEEP")]
		public void FluxDebugSelectedCoordsDeep()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			Database db = doc.Database;
			Editor ed = doc.Editor;

			PromptSelectionOptions pso = new PromptSelectionOptions();
			pso.MessageForAdding = "\n좌표를 분석할 객체들을 선택하세요: ";

			PromptSelectionResult psr = ed.GetSelection(pso);

			if (psr.Status != PromptStatus.OK)
			{
				ed.WriteMessage("\n선택이 취소되었습니다.");
				return;
			}

			ObjectId[] ids = psr.Value.GetObjectIds();

			using (Transaction tr = db.TransactionManager.StartTransaction())
			{
				ed.WriteMessage($"\n[DeepCoordDebug] SelectedCount={ids.Length}");

				int index = 0;

				foreach (ObjectId id in ids)
				{
					Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
					if (ent == null)
						continue;

					ed.WriteMessage($"\n\n[Selected #{index}] Id={id}, Type={ent.GetType().Name}, Layer={ent.Layer}");

					DumpEntity(ed, tr, ent, Matrix3d.Identity, 0);

					index++;
				}

				tr.Commit();
			}

			ed.WriteMessage("\n\n[DeepCoordDebug] 완료");
		}

		private static void DumpEntity(
			Editor ed,
			Transaction tr,
			Entity ent,
			Matrix3d worldTransform,
			int depth)
		{
			string indent = new string(' ', depth * 2);

			Bounds2D localBounds = BricscadEntityTools.GetEntityBounds(ent);
			Bounds2D worldBounds = TransformBounds(localBounds, worldTransform);

			ed.WriteMessage(
				$"\n{indent}[Entity] Type={ent.GetType().Name}, Layer={ent.Layer}");

			ed.WriteMessage(
				$"\n{indent}  LocalBounds={FormatBounds(localBounds)}");

			ed.WriteMessage(
				$"\n{indent}  WorldBounds={FormatBounds(worldBounds)}");

			if (ent is BlockReference br)
			{
				ed.WriteMessage(
					$"\n{indent}  [BlockReference] Name={GetBlockName(tr, br)}");

				ed.WriteMessage(
					$"\n{indent}  InsertPoint={FormatPoint(br.Position)}");

				ed.WriteMessage(
					$"\n{indent}  Scale=({br.ScaleFactors.X:0.###}, {br.ScaleFactors.Y:0.###}, {br.ScaleFactors.Z:0.###})");

				ed.WriteMessage(
					$"\n{indent}  Rotation={br.Rotation:0.########}");

				Matrix3d nextTransform = worldTransform * br.BlockTransform;

				ed.WriteMessage(
					$"\n{indent}  BlockTransform={FormatMatrix(br.BlockTransform)}");

				BlockTableRecord btr =
					tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;

				if (btr == null)
					return;

				int childIndex = 0;

				foreach (ObjectId childId in btr)
				{
					Entity child = tr.GetObject(childId, OpenMode.ForRead) as Entity;
					if (child == null)
						continue;

					ed.WriteMessage(
						$"\n{indent}  [Child #{childIndex}] Id={childId}");

					DumpEntity(ed, tr, child, nextTransform, depth + 1);

					childIndex++;
				}
			}
		}

		private static Bounds2D TransformBounds(Bounds2D bounds, Matrix3d transform)
		{
			if (bounds == null || !bounds.IsValid)
				return null;

			Point3d p1 = new Point3d(bounds.MinX, bounds.MinY, 0).TransformBy(transform);
			Point3d p2 = new Point3d(bounds.MaxX, bounds.MinY, 0).TransformBy(transform);
			Point3d p3 = new Point3d(bounds.MaxX, bounds.MaxY, 0).TransformBy(transform);
			Point3d p4 = new Point3d(bounds.MinX, bounds.MaxY, 0).TransformBy(transform);

			double minX = Math.Min(Math.Min(p1.X, p2.X), Math.Min(p3.X, p4.X));
			double minY = Math.Min(Math.Min(p1.Y, p2.Y), Math.Min(p3.Y, p4.Y));
			double maxX = Math.Max(Math.Max(p1.X, p2.X), Math.Max(p3.X, p4.X));
			double maxY = Math.Max(Math.Max(p1.Y, p2.Y), Math.Max(p3.Y, p4.Y));

			return new Bounds2D(minX, minY, maxX, maxY);
		}

		private static string GetBlockName(Transaction tr, BlockReference br)
		{
			try
			{
				BlockTableRecord btr =
					tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;

				return btr?.Name ?? "(null)";
			}
			catch
			{
				return "(error)";
			}
		}

		private static string FormatBounds(Bounds2D b)
		{
			if (b == null || !b.IsValid)
				return "(invalid)";

			return $"Min=({b.MinX:0.###}, {b.MinY:0.###}), Max=({b.MaxX:0.###}, {b.MaxY:0.###}), W={b.Width:0.###}, H={b.Height:0.###}";
		}

		private static string FormatPoint(Point3d p)
		{
			return $"({p.X:0.###}, {p.Y:0.###}, {p.Z:0.###})";
		}

		private static string FormatMatrix(Matrix3d m)
		{
			double[] a = m.ToArray();

			return
				$"[{a[0]:0.###}, {a[1]:0.###}, {a[2]:0.###}, {a[3]:0.###}; " +
				$"{a[4]:0.###}, {a[5]:0.###}, {a[6]:0.###}, {a[7]:0.###}; " +
				$"{a[8]:0.###}, {a[9]:0.###}, {a[10]:0.###}, {a[11]:0.###}; " +
				$"{a[12]:0.###}, {a[13]:0.###}, {a[14]:0.###}, {a[15]:0.###}]";
		}
	}
}