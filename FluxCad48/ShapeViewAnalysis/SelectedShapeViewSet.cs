using System.Collections.Generic;
namespace FluxCad48.ShapeViewAnalysis
{
	public sealed class SelectedShapeViewSet
	{
		public List<SheetEntity> GeometryEntities { get; private set; }
		public List<SheetEntity> DimensionEntities { get; private set; }
		public List<SheetEntity> TextEntities { get; private set; }
		public List<SheetEntity> UnknownEntities { get; private set; }

		public SelectedShapeViewSet()
		{
			GeometryEntities = new List<SheetEntity>();
			DimensionEntities = new List<SheetEntity>();
			TextEntities = new List<SheetEntity>();
			UnknownEntities = new List<SheetEntity>();
		}

		public int TotalCount
		{
			get
			{
				return GeometryEntities.Count
					+ DimensionEntities.Count
					+ TextEntities.Count
					+ UnknownEntities.Count;
			}
		}
	}
}