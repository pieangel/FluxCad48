using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using FluxCad48.CopiedSheetAnalysis.BorderConnectedFiltering;
using System.Collections.Generic;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;

namespace FluxCad48.Commands
{
	public class FluxDebugOutsideInRingFilterCommand
	{
		[CommandMethod("FLUX_DEBUG_OUTSIDE_IN_RING_FILTER")]
		public void FluxDebugOutsideInRingFilter()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			Editor ed = doc.Editor;
			Database db = doc.Database;

			PromptSelectionOptions pso = new PromptSelectionOptions();
			pso.MessageForAdding =
				"\nOutside-In Ring Filter를 실행할 복사된 단일 쉬트 내부 개체들을 선택하세요: ";

			PromptSelectionResult psr = ed.GetSelection(pso);

			if (psr.Status != PromptStatus.OK)
			{
				AppendLog(ed, "[OutsideInRingFilter] 선택이 취소되었습니다.");
				return;
			}

			ObjectId[] ids = psr.Value.GetObjectIds();

			Dictionary<ObjectId, Extents3d> entityBoundsMap =
				new Dictionary<ObjectId, Extents3d>();

			Extents3d? totalBounds = null;

			using (Transaction tr = db.TransactionManager.StartTransaction())
			{
				for (int i = 0; i < ids.Length; i++)
				{
					ObjectId id = ids[i];

					Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
					if (ent == null)
						continue;

					Extents3d? b = TryGetEntityBounds(ent);
					if (!b.HasValue)
						continue;

					entityBoundsMap[id] = b.Value;

					if (totalBounds.HasValue)
					{
						Extents3d merged = totalBounds.Value;
						merged.AddExtents(b.Value);
						totalBounds = merged;
					}
					else
					{
						totalBounds = b.Value;
					}
				}

				tr.Commit();
			}

			if (!totalBounds.HasValue)
			{
				AppendLog(ed, "[OutsideInRingFilter] 유효한 Bounds를 가진 개체가 없습니다.");
				return;
			}

			OutsideInRingFilter filter = new OutsideInRingFilter();

			OutsideInRingFilterResult result =
				filter.Analyze(
					totalBounds.Value,
					entityBoundsMap,
					5);

			AppendLog(ed, "========== Outside-In Ring Filter ==========");
			AppendLog(ed, "Selected Entity Count : " + ids.Length);
			AppendLog(ed, "Bounds Entity Count   : " + entityBoundsMap.Count);
			AppendLog(ed, "Ring Count            : " + result.Rings.Count);
			AppendLog(ed, "Excluded Count        : " + result.ExcludedEntityIds.Count);
			AppendLog(ed, "Inner Candidate Count : " + result.InnerCandidateIds.Count);

			AppendLog(ed, "");
			AppendLog(ed, "---------- Rings ----------");

			for (int i = 0; i < result.Rings.Count; i++)
			{
				RingBand ring = result.Rings[i];

				AppendLog(
					ed,
					"Ring[" + ring.RingIndex + "] " +
					"Outer=" + FormatBounds(ring.OuterBounds) +
					" Inner=" + FormatBounds(ring.InnerBounds));
			}

			AppendLog(ed, "");
			AppendLog(ed, "---------- Excluded Entity Handles ----------");

			PrintObjectIdHandles(ed, result.ExcludedEntityIds);

			AppendLog(ed, "");
			AppendLog(ed, "---------- Inner Candidate Handles ----------");

			PrintObjectIdHandles(ed, result.InnerCandidateIds);

			AppendLog(ed, "[OutsideInRingFilter] 완료.");
		}

		private static Extents3d? TryGetEntityBounds(Entity ent)
		{
			try
			{
				return ent.GeometricExtents;
			}
			catch
			{
				return null;
			}
		}

		private static void PrintObjectIdHandles(Editor ed, List<ObjectId> ids)
		{
			for (int i = 0; i < ids.Count; i++)
			{
				ObjectId id = ids[i];

				AppendLog(
					ed,
					"[" + i + "] Handle=" + id.Handle.ToString());
			}
		}

		private static string FormatBounds(Extents3d b)
		{
			return
				"Min=(" +
				b.MinPoint.X.ToString("0.##") + "," +
				b.MinPoint.Y.ToString("0.##") + ") " +
				"Max=(" +
				b.MaxPoint.X.ToString("0.##") + "," +
				b.MaxPoint.Y.ToString("0.##") + ")";
		}

		private static void AppendLog(Editor ed, string message)
		{
			ed.WriteMessage("\n" + message);
		}
	}
}