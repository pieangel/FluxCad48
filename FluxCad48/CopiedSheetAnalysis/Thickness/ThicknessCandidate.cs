using Teigha.DatabaseServices;

namespace FluxCad48.CopiedSheetAnalysis.Thickness
{
	internal class ThicknessCandidate
	{
		public ObjectId EntityAId { get; set; }
		public ObjectId EntityBId { get; set; }

		public double Value { get; set; }

		public double Confidence { get; set; }

		public string Source { get; set; }

		public ThicknessCandidate()
		{
			EntityAId = ObjectId.Null;
			EntityBId = ObjectId.Null;
			Source = "";
		}
	}
}