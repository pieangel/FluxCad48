using Teigha.DatabaseServices;
using Teigha.Geometry;
using FluxCad48.Geometry;

namespace FluxCad48.Brics
{
	public class WorldEntityInfo
	{
		public ObjectId SourceId { get; set; }
		public ObjectId OwnerBlockReferenceId { get; set; }

		public string EntityType { get; set; }
		public string Layer { get; set; }

		public Bounds2D LocalBounds { get; set; }
		public Bounds2D WorldBounds { get; set; }

		public Matrix3d AccumulatedTransform { get; set; }

		public int BlockDepth { get; set; }
		public bool IsInsideBlock => BlockDepth > 0;
	}
}