using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCad48.ShapeViewAnalysis
{
	public static class SelectedShapeViewClassifier
	{
		public static SelectedShapeViewSet Classify(IEnumerable<SheetEntity> entities)
		{
			SelectedShapeViewSet result = new SelectedShapeViewSet();

			if (entities == null)
				return result;

			foreach (SheetEntity entity in entities)
			{
				if (entity == null)
					continue;

				if (entity.IsDimensionLike)
				{
					result.DimensionEntities.Add(entity);
				}
				else if (entity.IsTextLike)
				{
					result.TextEntities.Add(entity);
				}
				else if (entity.IsGeometryLike)
				{
					result.GeometryEntities.Add(entity);
				}
				else
				{
					result.UnknownEntities.Add(entity);
				}
			}

			return result;
		}
	}
}
