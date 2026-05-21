using System.Collections.Generic;
using Teigha.DatabaseServices;
using FluxCad48.Geometry;

namespace FluxCad48.Sheets
{
	public class SheetRegion
	{
		public int Index { get; set; }

		public Bounds2D Bounds { get; set; }

		public List<ObjectId> EntityIds { get; private set; }

		public SheetRegion()
		{
			EntityIds = new List<ObjectId>();
		}
	}
}