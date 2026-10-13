using System;
using OpenCivOne.Graphics;

namespace OpenCivOne
{
	public class ContinentPolicy
	{
		public UnitStrategyTypeEnum UnitRoleType = UnitStrategyTypeEnum.Settle;
		/// <summary>
		/// !!! This setting appears not to be used anywhere. Perhaps we should join PlayerContinent with ContinentPolicy objects?
		/// </summary>
		public int Policy = 0;
		public GPoint Position = new GPoint(0, 0);
	}
}
