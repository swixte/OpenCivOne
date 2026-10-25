using Avalonia.Media;
using OpenCivOne.Graphics;
using System.Text;

namespace OpenCivOne
{
	public class GameEngine
	{
		private OpenCivOneGame parent;

		private bool populationHighRange = false;
		private int nextPopulationThreshold = 1;

		public GameEngine(OpenCivOneGame parent)
		{
			this.parent = parent;
		}

		/// <summary>
		/// Handles a single game turn.
		/// Performs active player's turns, checks for disasters, victory and loss conditions.
		/// </summary>
		public void F0_1238_0092_GameTurn()
		{
			//this.oCPU.Log.EnterBlock("F0_1238_0092_GameTurn()");

			// function body
			// Instruction address 0x1238:0x009f, size: 5
			this.parent.CommonTools.StartPaletteCycleSlots();

			Player humanPlayer = this.parent.GameData.Players[this.parent.GameData.HumanPlayerID];

			int oldCoins = humanPlayer.Coins;

			this.parent.Var_d760_HumanPlayerMessageFlag = false;
			this.parent.Var_6b92 = 0;
			this.parent.GameData.MaximumTechnologyCount = 0;

			for (int i = 0; i < 8; i++)
			{
				if ((this.parent.GameData.ActiveCivilizations & (0x1 << i)) != 0)
				{
					this.parent.GameData.MaximumTechnologyCount = Math.Max(this.parent.GameData.MaximumTechnologyCount, this.parent.GameData.Players[i].DiscoveredTechnologyCount);
				}
			}

			if (this.parent.GameData.TurnCount == 0)
			{
				// Show map around starting unit
				this.parent.Var_d4cc_MapViewX = humanPlayer.Units[0].Position.X - 7;
				this.parent.Var_d75e_MapViewY = humanPlayer.Units[0].Position.Y - 6;

				// Instruction address 0x1238:0x0119, size: 3
				F0_1238_1b44_RedrawMapWithBlankingEffect();

				if (this.parent.GameData.DifficultyLevel == 0)
				{
					// Instruction address 0x1238:0x012a, size: 5
					this.parent.PlayerTurn.F0_1403_4060_DrawStatusPanel(this.parent.GameData.HumanPlayerID, 0);

					// Show UI help on first turn
					this.parent.MainIntro.F2_0000_17d9_ShowInstantAdviceElements();

					// Instruction address 0x1238:0x0138, size: 3
					F0_1238_1b44_RedrawMapWithBlankingEffect();
				}
			
				// Instruction address 0x1238:0x013f, size: 5
				this.parent.CommonTools.PlayTune(1, 0);
			}

			for (int i = 0; i < 8; i++)
			{
				Player currentPlayer = this.parent.GameData.Players[i];
				currentPlayer.Score = 0;

				if ((this.parent.GameData.ActiveCivilizations & (0x1 << i)) != 0 &&
					(i >= this.parent.GameData.HumanPlayerID || this.parent.Var_df60 == 0))
				{
					// Blink 'wait' status at the bottom left corner of the screen
					// Instruction address 0x1238:0x0164, size: 5
					this.parent.DrawTools.FillRectangle(this.parent.Var_aa_Screen0_Rectangle, 2, 192, 6, 6, 8);

					if (this.parent.Var_df60 == 0)
					{
						// Instruction address 0x1238:0x018d, size: 5
						this.parent.GameEngine.F0_1ade_0006_ProcessCitiesAndAdjustTaxRates(i);
					}

					if (currentPlayer.Coins > 30000)
					{
						currentPlayer.Coins = 30000;
					}

					// Instruction address 0x1238:0x01ab, size: 5
					this.parent.Diplomacy.F0_2517_0004_UpdatePlayerGovernmentAndDiplomacy(i);

					// Instruction address 0x1238:0x01b6, size: 5
					this.parent.AIEngine.F0_25fb_0004_RecalculateStatsAndPolicies(i);

					// Instruction address 0x1238:0x01c1, size: 5
					this.parent.AIEngine.F0_25fb_2fd7_ClearUnitPoliciesAndReassignThem((short)i);

					// Blink 'wait' status
					// Instruction address 0x1238:0x01de, size: 5
					this.parent.DrawTools.FillRectangle(this.parent.Var_aa_Screen0_Rectangle, 2, 192, 6, 6, 15);

					if (i == this.parent.GameData.HumanPlayerID)
					{
						// Instruction address 0x1238:0x01ef, size: 3
						F0_1238_107e_ReportHumanTaxAndPopulation();

						if (currentPlayer.CityCount == 0 && currentPlayer.SettlerCount == 0)
						{
							// Human player lost the game
							this.parent.MainIntro.F2_0000_152a_EndGame_PlayerLost();

							// Game state = player lost (-1)
							this.parent.Var_dc48_GameEndType = -1;
						}

						if (this.parent.GameData.Year > 0)
						{
							parent.GameData.PeaceTurnCount++;
						}
					}

					// Check if game continues
					if (this.parent.Var_dc48_GameEndType == 0)
					{
						// Instruction address 0x1238:0x022f, size: 5
						this.parent.PlayerTurn.F0_1403_000e_PlayerTurn(i);
					}

					this.parent.Var_df60 = 0;

					// Check if game should continue with the next player
					if (this.parent.Var_dc48_GameEndType != 0 && this.parent.Var_b884 == 0)
					{
						if (this.parent.Var_dc48_GameEndType == 2)
						{
							// Human player decided to retire
							// Show power graph, civilization score and hall of fame

							// Instruction address 0x1238:0x0261, size: 3
							F0_1238_08a0_EndGame();

							// Instruction address 0x1238:0x0897, size: 3
							//F0_1238_090a_ShowAdvertisementsPopup();
						}

						return;
					}
				}
			}

			F0_1238_0980_GenerateBarbarianUprising();
			F0_1238_1767_CheckGlobalWarming();
			GiveWonderToAI();

			if (this.parent.GameData.TurnCount > 50)
			{
				for (int i = 0; i < 2; i++)
				{
					// Instruction address 0x1238:0x02d3, size: 5
					int cityID = this.parent.CAPI.RNG.Next(128);

					if (this.parent.GameData.Cities[cityID].Status != CityStatusEnum.Invalid &&
						this.parent.GameData.Players[this.parent.GameData.Cities[cityID].PlayerID].CityCount > 1)
					{
						if (this.parent.GameData.Cities[cityID].ActualSize >= 5)
						{
							// Process city disasters and riots
							this.parent.Overlay_20.F20_0000_0540((short)cityID);
						}
					}
				}
			}

			// Instruction address 0x1238:0x0307, size: 3
			F0_1238_0da1_RankPlayers();

			if (this.parent.Var_dc48_GameEndType == 0 && 
				humanPlayer.CityCount > 1 && humanPlayer.Coins < 100 && ((humanPlayer.Coins - oldCoins) * 10 + humanPlayer.Coins) < 0 &&
				(this.parent.GameData.PlayerFlags & (0x1 << this.parent.GameData.HumanPlayerID)) != 0)
			{
				// Warning: Funds are running low...
				this.parent.Var_2f9e_MessageBoxStyle = MenuBoxReportTypeEnum.DomesticAdvisorReport;

				// Instruction address 0x1238:0x0367, size: 3
				this.parent.MenuBoxDialog.ShowDialog(this.parent.LanguageTools.F0_2f4d_044f_GetTextFromKingSection("*TAXES"), 80, 80);
			}

			this.parent.GameData.TurnCount++;

			if (this.parent.GameData.Year < 1000)
			{
				this.parent.GameData.Year += 20;

				// handle case where after AD the year is set to 1 AD and then incremented by 20 equals 21 AD
				if (this.parent.GameData.Year == 21)
				{
					this.parent.GameData.Year = 20;
				}
			}
			else if (this.parent.GameData.Year < 1500)
			{
				this.parent.GameData.Year += 10;
			}
			else if (this.parent.GameData.Year < 1750)
			{
				this.parent.GameData.Year += 5;
			}
			else if (this.parent.GameData.Year < 1850 && (this.parent.GameData.SpaceshipFlags & 0xfe) == 0)
			{
				this.parent.GameData.Year += 2;
			}
			else
			{
				this.parent.GameData.Year += 1;
			}

			if (parent.GameData.Year == 0)
			{
				// boost research after AD event

				for (int i = 0; i < 8; i++)
				{
					this.parent.GameData.Players[i].ResearchProgress *= 2;
				}

				this.parent.GameData.Year = 1;
			}

			if ((this.parent.GameData.TurnCount % 50) == 0 && this.parent.GameData.HumanPlayerID != -1)
			{
				if (this.parent.GameData.GameSettingFlags.AutoSave)
				{
					// Game auto save
					this.parent.LoadAndSave.ShowSaveGameDialog(true);
				}

				this.parent.GameData.MaximumTechnologyCount = 0;

				for (int i = 1; i < 8; i++)
				{
					this.parent.GameData.MaximumTechnologyCount = Math.Max(this.parent.GameData.MaximumTechnologyCount, this.parent.GameData.Players[i].DiscoveredTechnologyCount);
				}

				this.parent.Var_d2de = 0;

				if (this.parent.GameData.Year >= 0)
				{
					// Instruction address 0x1238:0x0484, size: 5
					this.parent.Var_d2de = this.parent.CommonTools.CheckValueRange(
						this.parent.GameData.MaximumTechnologyCount - (this.parent.GameData.TurnCount / 9), 0, 6);
				}

				// Instruction address 0x1238:0x0493, size: 5
				int playerPopulation = this.parent.GameEngine.GetPlayerPopulationCount(this.parent.GameData.HumanPlayerID);

				// Instruction address 0x1238:0x04c8, size: 5
				this.parent.UnitManagement.F0_1866_250e_AddReplayData(11, (byte)humanPlayer.CityCount, (byte)((playerPopulation & 0xff00) >> 8), (byte)(playerPopulation & 0xff));

				int[] playerRankings = new int[4];

				Array.Fill(playerRankings, 0);

				for (int i = 1; i < 8; i++)
				{
					if ((this.parent.GameData.ActiveCivilizations & (0x1 << i)) != 0)
					{
						if ((this.parent.GameData.Players[i].Ranking & 0x1) == 0)
						{
							playerRankings[this.parent.GameData.Players[i].Ranking / 2] |= i;
						}
						else
						{
							playerRankings[this.parent.GameData.Players[i].Ranking / 2] |= i << 4;
						}
					}
				}

				// Instruction address 0x1238:0x0556, size: 5
				this.parent.UnitManagement.F0_1866_250e_AddReplayData(12, (byte)playerRankings[3], (byte)playerRankings[2], (byte)playerRankings[1], (byte)playerRankings[0]);
			}

			if (this.parent.GameData.TurnCount >= this.parent.GameData.NextPlayerHistoryRankingTurn)
			{
				// Instruction address 0x1238:0x056b, size: 5
				this.parent.GameData.NextPlayerHistoryRankingTurn = (short)(this.parent.CAPI.RNG.Next(40) + this.parent.GameData.TurnCount + 20);

				// Shows ranking report
				this.parent.Reports.F12_0000_09e2_PlayerHistoryRankingReport();
			}

			for (int i = 1; i < 22; i++)
			{
				int cityID = this.parent.GameData.WonderCityID[i];

				if (cityID != -1 && cityID < 128)
				{
					this.parent.GameData.Players[this.parent.GameData.Cities[cityID].PlayerID].Score += 25;
				}
			}

			if (this.parent.Var_3484 == -3)
			{
				this.parent.Var_3484 = -2;
			}
			else
			{
				humanPlayer.Score -= (short)(10 * this.parent.GameData.PollutedSquareCount);

				if (this.parent.Var_3484 == -2)
				{
					this.parent.Var_3484 = 0;
				}
			}

			#region For debugging, grow palace
			/*humanPlayer.PalaceBuildCount = 0;

			for (int i = 0; i < 12; i++)
			{
				humanPlayer.PalaceLevels[i] = (short)(((i > 2 && i < 6) || i > 8) ? 0 : -1);
				humanPlayer.PalaceTypes[i] = 0;
			}

			while (humanPlayer.PalaceBuildCount < 38)
			{
				this.oParent.Palace.F17_0000_0000_UpgradePalace(this.oParent.GameData.HumanPlayerID);

				humanPlayer.PalaceBuildCount++;
			}*/
			#endregion

			for (int i = 0; i < 128; i++)
			{
				if (this.parent.GameData.Cities[i].Status != CityStatusEnum.Invalid &&
					this.parent.GameData.Cities[i].PlayerID == this.parent.GameData.HumanPlayerID &&
					(this.parent.GameData.Cities[i].ImprovementFlags0 & 0x1) != 0)
				{
					if (this.parent.GameData.GameSettingFlags.BuildPalace && (this.parent.GameData.SpaceshipFlags & 0x100) == 0 &&
						(short)this.parent.Overlay_20.F20_0000_0ca9_ShowCivilizationScorePopup(this.parent.GameData.HumanPlayerID, false) >= F0_1238_16e7_GetPalaceBuildNewScoreThreshold(humanPlayer.PalaceBuildCount + 1))
					{
						humanPlayer.PalaceBuildCount++;

						if (humanPlayer.PalaceBuildCount < 38)
						{
							// build palace user dialog
							this.parent.Palace.F17_0000_0000_UpgradePalace(this.parent.GameData.HumanPlayerID);
						}
					}
					// No need to check any further
					break;
				}
			}
		
			// Instruction address 0x1238:0x066f, size: 3
			F0_1238_107e_ReportHumanTaxAndPopulation();

			// ID of player that reached Alpha Centauri
			int playerIDSpaceVictory = -1;

			for (int i = 1; i < 8; i++)
			{
				if ((this.parent.GameData.SpaceshipFlags & (0x1 << i)) != 0 &&
					this.parent.GameData.Players[i].SpaceshipETAYear == this.parent.GameData.Year)
				{
					playerIDSpaceVictory = i;
					// no need to check any further
					break;
				}
			}

			if (this.parent.GameData.Year >= 2080 - (20 * this.parent.GameData.DifficultyLevel))
			{
				this.parent.News.F21_0000_0000_ShowNews(-1,
					$"{this.parent.Array_19b2_GovernmentTypeNames[(int)humanPlayer.GovernmentType]} {humanPlayer.Name}\nplans retirement\nin 20 years.\n");
			}

			if (playerIDSpaceVictory != -1 || this.parent.Var_b884 != 0 ||
				this.parent.GameData.Year >= 2100 - (20 * this.parent.GameData.DifficultyLevel))
			{
				if (this.parent.Var_b884 != 0)
				{
					// Conquest victory, don't care about space race
					playerIDSpaceVictory = -1;

					this.parent.News.F21_0000_0000_ShowNews(-1, "*** GAME OVER ***\nYour civilization\nhas conquered\nthe entire planet!\n");

					this.parent.Replay.F9_0000_0dde();
				}

				if (playerIDSpaceVictory != -1)
				{
					this.parent.MainIntro.F2_0000_0bd7_EndGame_SpaceVictory((short)playerIDSpaceVictory);

					// Instruction address 0x1238:0x0792, size: 3
					F0_1238_1b44_RedrawMapWithBlankingEffect();

					this.parent.News.F21_0000_0000_ShowNews(-1,
						$"*** GAME OVER ***\n{this.parent.GameData.Players[playerIDSpaceVictory].Nationality} spaceship\narrives at Alpha Centauri.\n");
				}

				if (this.parent.GameData.Year >= 2100 - (20 * this.parent.GameData.DifficultyLevel))
				{
					// Instruction address 0x1238:0x0816, size: 5
					this.parent.CommonTools.PlayTune(this.parent.GameData.Nations[humanPlayer.NationalityID].LongTune, 3);

					this.parent.News.F21_0000_0000_ShowNews(-1, $"*** GAME OVER ***\n{humanPlayer.Name} dynasty\nends after glorious\n6000 year reign!\n");

					// Instruction address 0x1238:0x082e, size: 5
					this.parent.CommonTools.PlayTune(1, 0);
				}

				if ((this.parent.GameData.SpaceshipFlags & 0x100) == 0)
				{
					// Show power graph, civilization score and hall of fame
					F0_1238_08a0_EndGame();
				}

				// Instruction address 0x1238:0x084f, size: 3
				F0_1238_1b44_RedrawMapWithBlankingEffect();

				if (this.parent.Var_b884 == 0)
				{
					// Do you want to keep playing ?
					this.parent.Var_dc48_GameEndType = 1 - this.parent.MenuBoxDialog.ShowDialog(this.parent.LanguageTools.F0_2f4d_044f_GetTextFromKingSection("*FAME"), 80, 80);
				}
				else
				{
					this.parent.Var_dc48_GameEndType = 1;
				}
			}
		}

		/// <summary>
		/// End game
		/// </summary>
		public void F0_1238_08a0_EndGame()
		{
			//this.oCPU.Log.EnterBlock("F0_1238_08a0()");

			// function body
			this.parent.Reports.F12_0000_0573_ShowPowerGraphReport();

			if (this.parent.Var_dc48_GameEndType != 2)
			{
				this.parent.Replay.F9_0000_0000();
			}

			if ((this.parent.GameData.SpaceshipFlags & 0x100) == 0)
			{
				this.parent.HallOfFame.F3_0000_065d_ShowFamousPersonsWindow((this.parent.GameData.Players[this.parent.GameData.HumanPlayerID].PalaceBuildCount / 2) - 1,
					(short)this.parent.Overlay_20.F20_0000_0ca9_ShowCivilizationScorePopup(this.parent.GameData.HumanPlayerID, true));

				this.parent.HallOfFame.F3_0000_002b_ReadHallOfFameFromFile();

				this.parent.HallOfFame.F3_0000_0513_AddNewEntryToHallOfFame();

				this.parent.HallOfFame.F3_0000_0083_WriteHallOfFameToFile();
			}		
		}

		/// <summary>
		/// Generates barbarian uprisings in the random area in the beginning of the game.
		/// At first barbarian uprisings are near the cities and after the discovery of Automobile they are everywhere
		/// </summary>
		public void F0_1238_0980_GenerateBarbarianUprising()
		{
			//this.oCPU.Log.EnterBlock("F0_1238_0980()");

			// function body
			if ((5 - this.parent.GameData.DifficultyLevel) * 30 <= this.parent.GameData.TurnCount)
			{
				if (((this.parent.GameData.TurnCount + 1) & 0x7) == 0 &&
					(this.parent.GameData.TechnologyFirstDiscoveredBy[(int)TechnologyAdvanceEnum.Automobile] & 0x7) == 0)
				{
					int x;
					int y;

					do
					{
						x = this.parent.CAPI.RNG.Next(80);
						y = this.parent.CAPI.RNG.Next(44) + 3;
					}
					while (this.parent.MapManagement.GetTerrainType(x, y) != TerrainTypeEnum.Water ||
						this.parent.MapManagement.F0_2aea_14e0_GetCellActiveUnitPlayerID(x, y) != -1 ||
						this.parent.MapManagement.F0_2aea_195d_GetMapGroupSize(x, y) < 16);

					int barbarianUnitCount = this.parent.CommonTools.CheckValueRange((this.parent.GameData.TurnCount / 150) + 1, 1, 3);

					// Instruction address 0x1238:0x0a43, size: 5
					if (this.parent.MapManagement.GetTerrainType(x, y) == TerrainTypeEnum.Water)
					{
						this.parent.UnitManagement.F0_1866_0cf5_CreateUnit(0, ((barbarianUnitCount < 3) ? UnitTypeEnum.Sail : UnitTypeEnum.Frigate), x, y);
					}

					for (int i = 0; i <= barbarianUnitCount; i++)
					{
						UnitTypeEnum unitType;

						if ((this.parent.GameData.TechnologyFirstDiscoveredBy[(int)TechnologyAdvanceEnum.Explosives] & 0x7) == 0)
						{
							unitType = UnitTypeEnum.Legion;
						}
						else
						{
							unitType = UnitTypeEnum.Knights;
						}

						// Last barbarian unit is always a diplomat
						if (i == barbarianUnitCount)
						{
							unitType = UnitTypeEnum.Diplomat;
						}

						int unitID = this.parent.UnitManagement.F0_1866_0cf5_CreateUnit(0, unitType, x, y);

						this.parent.GameData.Players[0].Units[unitID].Status |= UnitStatusEnum.Sentry;
					}
				}

				int selectedCityID = this.parent.CAPI.RNG.Next(128);

				if (this.parent.GameData.Cities[selectedCityID].Status != CityStatusEnum.Invalid &&
					this.parent.GameData.Cities[selectedCityID].PlayerID == this.parent.GameData.HumanPlayerID &&
					this.parent.CAPI.RNG.Next(100) < this.parent.Segment_2459.F0_2459_0687_GetCityTreasury(selectedCityID))
				{
					int x = 0;
					int y = 0;
					int retryCount;

					for (retryCount = 0; retryCount < 99; retryCount++)
					{
						x = this.parent.GameData.Cities[selectedCityID].Position.X + this.parent.CAPI.RNG.Next(13) - 6;
						y = this.parent.GameData.Cities[selectedCityID].Position.Y + this.parent.CAPI.RNG.Next(13) - 6;

						selectedCityID = this.parent.MapManagement.FindNearestCity(x, y);

						int distance = this.parent.MapManagement.GetDistance(x, y, this.parent.GameData.Cities[selectedCityID].Position);

						if (distance >= 6)
						{
							if (!this.parent.UnitManagement.F0_1866_1750_IsUnitOrCityNear(0, x, y) &&
								this.parent.MapManagement.GetTerrainType(x, y) != TerrainTypeEnum.Water &&
								this.parent.MapManagement.F0_2aea_14e0_GetCellActiveUnitPlayerID(x, y) == -1 &&
								this.parent.MapManagement.F0_2aea_1942_GetGroupID(x, y) == this.parent.MapManagement.F0_2aea_1942_GetGroupID(
									this.parent.GameData.Cities[selectedCityID].Position.X, this.parent.GameData.Cities[selectedCityID].Position.Y))
								break;
						}
					}

					if (retryCount < 99)
					{
						if (this.parent.GameData.Cities[selectedCityID].PlayerID == this.parent.GameData.HumanPlayerID &&
							(this.parent.GameData.MapVisibility[x, y] & (0x1 << this.parent.GameData.HumanPlayerID)) != 0)
						{
							// Instruction address 0x1238:0x0c4b, size: 5
							this.parent.MapManagement.F0_2aea_0008_DrawVisibleMap(this.parent.GameData.HumanPlayerID,
								this.parent.GameData.Cities[selectedCityID].Position.X - 8, this.parent.GameData.Cities[selectedCityID].Position.Y - 6);

							this.parent.Var_2f9e_MessageBoxStyle = MenuBoxReportTypeEnum.TravelersReport;

							if ((this.parent.GameData.TechnologyFirstDiscoveredBy[35] & 7) != 0)
							{
								this.parent.MenuBoxDialog.ShowDialog(
									$"Travellers report:\nGuerrilla uprising\nnear {this.parent.Segment_2459.F0_2459_08c6_GetCityName(selectedCityID)}!\n", 100, 32);
							}
							else if ((this.parent.GameData.TechnologyFirstDiscoveredBy[68] & 7) != 0)
							{
								this.parent.MenuBoxDialog.ShowDialog(
									$"Travellers report:\nNative unrest\nnear {this.parent.Segment_2459.F0_2459_08c6_GetCityName(selectedCityID)}!\n", 100, 32);
							}
							else
							{
								this.parent.MenuBoxDialog.ShowDialog(
									$"Travellers report:\nBarbarian uprising\nnear {this.parent.Segment_2459.F0_2459_08c6_GetCityName(selectedCityID)}!\n", 100, 32);
							}
						}
					
						// 99 units? Isn't that a little too big, the city would actually be the size of 198 to reach that count. Is that even possible?
						int barbarianUnitCount = this.parent.CommonTools.CheckValueRange(this.parent.CAPI.RNG.Next(this.parent.GameData.Cities[selectedCityID].ActualSize / 2), 2, 99);

						for (int i = 0; i <= barbarianUnitCount; i++)
						{
							UnitTypeEnum unitType;

							if ((this.parent.GameData.TechnologyFirstDiscoveredBy[(int)TechnologyAdvanceEnum.Industrialization] & 0x7) == 0)
							{
								if ((i & 0x4) != 0)
								{
									unitType = UnitTypeEnum.Chariot;
								}
								else
								{
									unitType = UnitTypeEnum.Cavalry;
								}
							}
							else
							{
								if ((i & 0x4) == 0)
								{
									unitType = UnitTypeEnum.Musketeers;
								}
								else
								{
									unitType = UnitTypeEnum.Cannon;
								}
							}

							if (i == barbarianUnitCount)
							{
								unitType = UnitTypeEnum.Diplomat;
							}

							int unitID = this.parent.UnitManagement.F0_1866_0cf5_CreateUnit(0, unitType, x, y);

							// In case our barbarian unit count has reached capacity
							if (unitID != -1)
							{
								this.parent.GameData.Players[0].Units[unitID].VisibleByPlayer |= this.parent.GameData.MapVisibility[x, y];
							}

							if (this.parent.GameData.Cities[selectedCityID].PlayerID == this.parent.GameData.HumanPlayerID)
							{
								// Instruction address 0x1238:0x0d6a, size: 5
								this.parent.MapManagement.F0_2aea_11d4_DrawCellWithUnit(x, y);
							}
						}
					}
				}
			}
		}

		/// <summary>
		/// Rank players and trigger contact with Human player (if it has lowest ranking)
		/// </summary>
		public void F0_1238_0da1_RankPlayers()
		{
			// this.CPU.Log.EnterBlock("F0_1238_0da1()");

			// function body
			int[] rankingScores = new int[8];

			for (int i = 1; i < 8; i++)
			{
				Player player = this.parent.GameData.Players[i];

				rankingScores[i] = player.Coins / 32 + player.TotalCitySize * 8 + player.DiscoveredTechnologyCount;

				for (int j = 0; j < 28; j++)
				{
					rankingScores[i] += this.parent.GameData.Units[j].Cost * player.ActiveUnits[j];
				}

				if ((this.parent.GameData.ActiveCivilizations & (0x1 << i)) == 0)
				{
					rankingScores[i] = 0;
				}

				if (this.parent.GameData.TurnCount < 600)
				{
					this.parent.GameData.ScoreGraphData[((this.parent.GameData.TurnCount / 4) % 150) * 8 + i] = (byte)this.parent.CommonTools.CheckValueRange(rankingScores[i] / 8, 0, 255);
				}
			
				int peaceFlags = 0;

				for (int j = 0; j < 8; j++)
				{
					if ((player.Diplomacy[j] & (DiplomacyFlagsEnum.Contact | DiplomacyFlagsEnum.Peace)) == DiplomacyFlagsEnum.Contact)
					{
						peaceFlags |= (0x1 << j);
					}
				}

				if (this.parent.GameData.TurnCount < 600)
				{
					this.parent.GameData.PeaceGraphData[((this.parent.GameData.TurnCount / 4) % 150) * 8 + i] = (byte)peaceFlags;
				}
			}

			for (int i = 1; i < 8; i++)
			{
				int selectedPlayerID = 0;
				int highestScore = -1;

				for (int j = 1; j < 8; j++)
				{
					if (highestScore < rankingScores[j])
					{
						selectedPlayerID = j;
						highestScore = rankingScores[j];
					}
				}

				if (selectedPlayerID < this.parent.GameData.Players.Length)
				{
					this.parent.GameData.Players[selectedPlayerID].Ranking = (short)(8 - i);
				}

				rankingScores[selectedPlayerID] = -1;
			}
			
			this.parent.Var_8078 = 0;

			// If the human player is ranked as lowest trigger contact with other players
			if (this.parent.GameData.Players[this.parent.GameData.HumanPlayerID].Ranking == 7 &&
				this.parent.GameData.Players[this.parent.GameData.HumanPlayerID].CityCount > 4 &&
				this.parent.GameData.Players[this.parent.GameData.HumanPlayerID].ActiveUnits[(int)UnitTypeEnum.Nuclear] == 0 &&
				this.parent.GameData.TurnCount > 200)
			{
				this.parent.Var_8078 = 1;

				for (int i = 1; i < 8; i++)
				{
					if (i != this.parent.GameData.HumanPlayerID)
					{
						if (this.parent.GameData.Players[i].CityCount > 1)
						{
							if (!this.parent.GameData.Players[i].Diplomacy[this.parent.GameData.HumanPlayerID].HasFlag(DiplomacyFlagsEnum.Peace))
							{
								this.parent.GameData.Players[i].Diplomacy[this.parent.GameData.HumanPlayerID] |= DiplomacyFlagsEnum.Contact;
							}
							else
							{
								this.parent.GameData.Players[i].Diplomacy[this.parent.GameData.HumanPlayerID] |= DiplomacyFlagsEnum.PeaceTreaty;
							}
						}
					}
				}
			}
		}

		/// <summary>
		/// Report tax and population values for human player
		/// </summary>
		public void F0_1238_107e_ReportHumanTaxAndPopulation()
		{
			//this.CPU.Log.EnterBlock("F0_1238_107e()");

			// function body			
			// Instruction address 0x1238:0x109f, size: 3
			this.parent.DrawTools.F0_1238_1bb2_FillRectangleWithShadow(0, 58, 80, 39);

			// Instruction address 0x1238:0x10ba, size: 5
			this.parent.DrawTools.FillRectangle(this.parent.Var_aa_Screen0_Rectangle, 3, 60, 74, 11, 11);

			if (this.parent.GameData.PeaceTurnCount > 1)
			{
				// Instruction address 0x1238:0x10f3, size: 5
				this.parent.DrawTools.FillRectangle(this.parent.Var_aa_Screen0_Rectangle, 3, 60, this.parent.CommonTools.CheckValueRange(this.parent.GameData.PeaceTurnCount * 2, 0, 60), 2, 15);
			}

			// Instruction address 0x1238:0x110c, size: 3
			F0_1238_14a3_DrawMiniPalace(this.parent.GameData.HumanPlayerID, this.parent.GameData.Players[this.parent.GameData.HumanPlayerID].PalaceBuildCount, 8, 58);

			int populationCount = this.parent.GameEngine.GetPlayerPopulationCount(this.parent.GameData.HumanPlayerID);

			if (populationCount != 0)
			{
				// Instruction address 0x1238:0x1196, size: 5
				this.parent.DrawTools.DrawStringToScreen0($"{this.parent.GameEngine.PopulationValueToString(populationCount)}#", 2, 73, 0);
			}

			// Instruction address 0x1238:0x11b6, size: 5
			this.parent.DrawTools.DrawStringToScreen0(YearToString(this.parent.GameData.Year), 2, 81, 0);

			int researchValue;

			if (this.parent.GameData.Year < 0)
			{
				researchValue = (this.parent.GameData.Players[this.parent.GameData.HumanPlayerID].ResearchProgress * 2) /
					((this.parent.GameData.DifficultyLevel * 2 + this.parent.Var_d2de + 6) *
						this.parent.GameData.Players[this.parent.GameData.HumanPlayerID].DiscoveredTechnologyCount + 1);
			}
			else
			{
				researchValue = (this.parent.GameData.Players[this.parent.GameData.HumanPlayerID].ResearchProgress * 2) /
					((this.parent.GameData.DifficultyLevel * 2 + this.parent.Var_d2de + 6) *
						this.parent.GameData.Players[this.parent.GameData.HumanPlayerID].DiscoveredTechnologyCount * 2 + 1);
			}

			// Instruction address 0x1238:0x1229, size: 5
			this.parent.Graphics.F0_VGA_07d8_DrawImage(this.parent.Var_19e8_Screen2_Rectangle,
				(this.parent.CommonTools.CheckValueRange(researchValue, 0, 3) * 8) + 160, 120, 8, 8, this.parent.Var_aa_Screen0_Rectangle, 48, 80);

			if (this.parent.GameData.PollutionEffectLevel != 0)
			{
				// Instruction address 0x1238:0x1280, size: 5
				this.parent.Graphics.F0_VGA_07d8_DrawImage(this.parent.Var_19e8_Screen2_Rectangle,
					(this.parent.CommonTools.CheckValueRange(this.parent.GameData.PollutionEffectLevel / 4, 0, 3) * 8) + 160, 128, 8, 8, this.parent.Var_aa_Screen0_Rectangle, 56, 80);
			}

			// Instruction address 0x1238:0x1382, size: 5
			this.parent.DrawTools.DrawStringToScreen0($"{this.parent.GameData.Players[this.parent.GameData.HumanPlayerID].Coins}$ " +
				$"{10 - this.parent.GameData.Players[this.parent.GameData.HumanPlayerID].TaxRate + this.parent.GameData.Players[this.parent.GameData.HumanPlayerID].ScienceTaxRate}(" +
				$"{this.parent.GameData.Players[this.parent.GameData.HumanPlayerID].TaxRate}/{this.parent.GameData.Players[this.parent.GameData.HumanPlayerID].ScienceTaxRate})", 2, 89, 0);

			int oldPopulationThreshold = this.nextPopulationThreshold;

			if (populationCount < 100)
			{
				if (this.nextPopulationThreshold * 10 < populationCount)
				{
					this.nextPopulationThreshold = populationCount / 10;
				}
			}
			else
			{
				if (!this.populationHighRange || this.nextPopulationThreshold * 100 < populationCount)
				{
					this.nextPopulationThreshold = populationCount / 100;
					this.populationHighRange = true;
				}
			}

			if (oldPopulationThreshold < this.nextPopulationThreshold && this.parent.Var_3484 >= -1)
			{
				this.parent.Var_2f9e_MessageBoxStyle = MenuBoxReportTypeEnum.DomesticAdvisorReport;

				// Instruction address 0x1238:0x1498, size: 3
				this.parent.MenuBoxDialog.ShowDialog($"The population of the\n{this.parent.GameData.Players[this.parent.GameData.HumanPlayerID].Nationality} " +
					$"empire now\nexceeds {this.parent.GameEngine.PopulationValueToString(populationCount)} citizens.\n", 100, 80);
			}
		}

		/// <summary>
		/// Draws mini palace on main status board, and on scoring page
		/// </summary>
		/// <param name="playerID"></param>
		/// <param name="palaceBuildCount"></param>
		/// <param name="x"></param>
		/// <param name="y"></param>
		public void F0_1238_14a3_DrawMiniPalace(int playerID, int palaceBuildCount, int x, int y)
		{
			//this.CPU.Log.EnterBlock($"F0_1238_14a3({playerID}, {palaceLevel}, {x}, {y})");

			// function body
			int[] palaceLevels = new int[9];
			int[] palaceTypes = new int[9];

			// Instruction address 0x1238:0x14c5, size: 5
			this.parent.Graphics.F0_VGA_0599_DrawLine(this.parent.Var_aa_Screen0_Rectangle, x - 5, y + 13, x + 68, y + 13, 2);

			if (this.parent.GameData.Players[playerID].CityCount != 0)
			{
				for (int i = 0; i < 9; i++)
				{
					palaceLevels[i] = -1;
				}

				for (int i = 3; i < 6; i++)
				{
					palaceLevels[i] = 0;
				}

				if (playerID == this.parent.GameData.HumanPlayerID)
				{
					for (int i = 0; i < 9; i++)
					{
						palaceLevels[i] = this.parent.GameData.Players[this.parent.GameData.HumanPlayerID].PalaceLevels[i];
						palaceTypes[i] = this.parent.GameData.Players[this.parent.GameData.HumanPlayerID].PalaceTypes[i];
					}
				}
				else
				{
					int miniPalaceLevel = this.parent.CommonTools.CheckValueRange(palaceBuildCount + 3, 3, 36);
					int miniPalaceThreshold = miniPalaceLevel / 5;
					int randomValue = playerID * this.parent.GameData.RandomSeed;

					for (int i = 0; i < miniPalaceLevel; i++)
					{
						int palaceLevel = 0;
						int palaceSection = 0;

						for (int j = 0; j < 999; j++)
						{
							bool palaceSectionFinished = true;

							randomValue = randomValue * 5 + 1;
							palaceSection = randomValue % 9;
							palaceLevel = palaceLevels[palaceSection] + 1;

							if (palaceSection < 4 && (palaceLevels[palaceSection + 1] < palaceLevel || palaceLevels[7 - palaceSection] == -1))
							{
								palaceSectionFinished = false;
							}

							if (palaceSection > 4 && (palaceLevels[palaceSection - 1] < palaceLevel || palaceLevels[9 - palaceSection] == -1))
							{
								palaceSectionFinished = false;
							}

							if (palaceLevel > miniPalaceThreshold || palaceLevel > 3)
							{
								palaceSectionFinished = false;
							}

							if (palaceSectionFinished)
								break;
						}

						palaceLevels[palaceSection] = palaceLevel;
						palaceTypes[palaceSection] = playerID % 3;
					}
				}

				for (int i = 0; i < 9; i++)
				{
					if (palaceLevels[i] > 0)
					{
						int palaceSection = 2; // Center section

						if (i < 4)
						{
							palaceSection = 1; // inner piece

							if (i == 1 || palaceLevels[i - 1] == -1)
							{
								palaceSection = 0; // Left edge
							}
						}

						if (i > 4)
						{
							palaceSection = 3; // inner piece

							if (i == 7 || palaceLevels[i + 1] == -1)
							{
								palaceSection = 4; // right edge
							}
						}

						// Instruction address 0x1238:0x16cd, size: 5
						this.parent.Graphics.F0_VGA_0c3e_DrawBitmapToScreen(this.parent.Var_aa_Screen0_Rectangle,
							(i * 7) + x, y - 1,
							this.parent.Array_d21c[palaceTypes[i], this.parent.CommonTools.CheckValueRange(palaceLevels[i] - 1, 0, 3) * 5 + palaceSection]);
					}
				}
			}
		}

		/// <summary>
		/// Gets the next score threshold to build the palace
		/// </summary>
		/// <param name="palaceLevel"></param>
		public int F0_1238_16e7_GetPalaceBuildNewScoreThreshold(int palaceLevel)
		{
			//this.CPU.Log.EnterBlock($"F0_1238_16e7({palaceLevel})");

			// function body
			int thresholdValue = 0;

			for (int i = 0; i <= palaceLevel; i++)
			{
				thresholdValue += i * (7 - this.parent.GameData.DifficultyLevel);
			}

			return thresholdValue / 2 + 1;
		}

		/// <summary>
		/// Converts year to String
		/// </summary>
		public string YearToString(int year)
		{
			return $"{Math.Abs(year)} {((year < 0) ? "BC" : "AD")}";
		}

		/// <summary>
		/// Convert the turn count to current year
		/// </summary>
		/// <param name="turnCount"></param>
		/// <returns></returns>
		public int TurnCountToYear(int turnCount)
		{
			//this.oCPU.Log.EnterBlock($"F9_0000_097c({turnCount})");
			int year;

			// function body
			if (turnCount > 250)
			{
				turnCount -= 250;

				if (turnCount > 50)
				{
					turnCount -= 50;

					if (turnCount > 50)
					{
						turnCount -= 50;

						if (turnCount > 50)
						{
							turnCount -= 50;

							year = turnCount + 1850;
						}
						else
						{
							year = (turnCount * 2) + 1750;
						}
					}
					else
					{
						year = (turnCount * 5) + 1500;
					}
				}
				else
				{
					year = (turnCount * 10) + 1000;
				}
			}
			else
			{
				year = (turnCount * 20) - 4000;
			}

			return year;
		}

		/// <summary>
		/// Check global warming and pollution
		/// </summary>
		public void F0_1238_1767_CheckGlobalWarming()
		{
			//this.CPU.Log.EnterBlock("F0_1238_1767()");

			// function body
			this.parent.GameData.PollutionEffectLevel = (short)this.parent.CommonTools.CheckValueRange(
				Math.Sign(this.parent.GameData.PollutedSquareCount * 2 - this.parent.GameData.GlobalWarmingCount * 4 - this.parent.GameData.PollutionEffectLevel) +
					this.parent.GameData.PollutionEffectLevel, 0, 99);

			if (this.parent.GameData.PollutionEffectLevel == 12 && this.parent.GameData.PollutedSquareCount > 6)
			{
				this.parent.News.F21_0000_0000_ShowNews(-1, "Scientists alarmed by\nrising temperatures.\nGlobal Warming feared!\n");
			}

			if (this.parent.GameData.PollutionEffectLevel > 16)
			{
				F7_0000_1be3_IcecapMeltEvent(this.parent.GameData.GlobalWarmingCount);

				this.parent.GameData.GlobalWarmingCount++;
				this.parent.GameData.PollutionEffectLevel = 0;
			}
		}

		/// <summary>
		/// Gives a wonder to any AI by chance
		/// </summary>
		public void GiveWonderToAI()
		{
			for (int i = 0; i <= this.parent.GameData.DifficultyLevel; i++)
			{
				int wonderID = this.parent.CAPI.RNG.Next(21) + 1;

				if (this.parent.GameData.DifficultyLevel != 0 ||
					this.parent.GameEngine.F0_1ade_22b5_PlayerHasTechnology(this.parent.GameData.HumanPlayerID,
						this.parent.GameData.GetImprovementType(wonderID).RequiresTechnology))
				{
					if (this.parent.GameData.DifficultyLevel < 2)
					{
						for (int j = 0; j < 128; j++)
						{
							if (this.parent.GameData.Cities[j].Status != CityStatusEnum.Invalid &&
								this.parent.GameData.Cities[j].PlayerID == this.parent.GameData.HumanPlayerID &&
								this.parent.GameData.Cities[j].CurrentProductionID == -(wonderID + 24))
							{
								wonderID = 17;
							}
						}
					}

					// Instruction address 0x1238:0x1928, size: 5
					int cityID = this.parent.CAPI.RNG.Next(128);

					if (this.parent.GameData.Cities[cityID].Status != CityStatusEnum.Invalid)
					{
						int playerID = this.parent.GameData.Cities[cityID].PlayerID;

						if (playerID != -1 && playerID != this.parent.GameData.HumanPlayerID &&
							!this.parent.GameData.Players[playerID].Diplomacy[0].HasFlag(DiplomacyFlagsEnum.Allied) &&
							(wonderID != 17 || this.parent.GameEngine.F0_1ade_22b5_PlayerHasTechnology(playerID, TechnologyAdvanceEnum.Rocketry)))
						{
							if (i == 0 &&
								this.parent.GameEngine.F0_1ade_22b5_PlayerHasTechnology(playerID, TechnologyAdvanceEnum.Rocketry) &&
								!this.parent.GameEngine.F0_1ade_22b5_PlayerHasTechnology(this.parent.GameData.HumanPlayerID, TechnologyAdvanceEnum.Rocketry) &&
								this.parent.GameData.WonderCityID[17] == -1)
							{
								wonderID = 17;
							}

							if ((wonderID != 17 ||
									!this.parent.GameEngine.F0_1ade_22b5_PlayerHasTechnology(this.parent.GameData.HumanPlayerID, TechnologyAdvanceEnum.Rocketry) ||
									this.parent.GameData.Players[playerID].MilitaryPower <= this.parent.GameData.Players[this.parent.GameData.HumanPlayerID].MilitaryPower) &&
								this.parent.GameData.WonderCityID[wonderID] == -1 &&
								this.parent.GameData.Cities[cityID].ActualSize > this.parent.GameData.Wonders[wonderID].Cost / 10 &&
								this.parent.GameEngine.F0_1ade_22b5_PlayerHasTechnology(playerID, this.parent.GameData.Wonders[wonderID].RequiresTechnology))
							{
								this.parent.GameData.WonderCityID[wonderID] = (short)cityID;

								this.parent.GameData.Cities[cityID].ShieldsCount = 0;

								// Instruction address 0x1238:0x1a6f, size: 5
								this.parent.UnitManagement.F0_1866_250e_AddReplayData(10, (byte)((sbyte)playerID), (byte)((sbyte)wonderID));

								this.parent.GameData.Players[playerID].Coins -= (short)this.parent.CommonTools.CheckValueRange(this.parent.GameData.Players[playerID].Coins / 3, 0,
									(10 * this.parent.GameData.Wonders[wonderID].Cost) - this.parent.GameData.Cities[cityID].ShieldsCount);

								// Instruction address 0x1238:0x1b1e, size: 5
								this.parent.CommonTools.PlayTune(this.parent.GameData.Nations[this.parent.GameData.Players[playerID].NationalityID].ShortTune, 0);

								if (!this.parent.GameData.Players[this.parent.GameData.HumanPlayerID].Diplomacy[playerID].HasFlag(DiplomacyFlagsEnum.EmbassyEstablished))
								{
									this.parent.Var_2f9e_MessageBoxStyle = MenuBoxReportTypeEnum.TravelersReport;
								}
								else
								{
									this.parent.Var_2f9e_MessageBoxStyle = MenuBoxReportTypeEnum.ForeignMinisterReport;
								}

								// Instruction address 0x1238:0x1802, size: 3
								this.parent.MenuBoxDialog.ShowDialog($"{this.parent.Segment_2459.F0_2459_08c6_GetCityName(cityID)} ({this.parent.GameData.Players[playerID].Nationality})\nbuilds " +
									$"{this.parent.GameData.Wonders[wonderID].Name}.\n", 100, 80);

								this.parent.Encyclopedia.DisplayEncyclopediaTopic(EncyclopediaTopicEnum.CityImprovement, wonderID + 24);

								// Instruction address 0x1238:0x181c, size: 3
								F0_1238_1b44_RedrawMapWithBlankingEffect();

								// Instruction address 0x1238:0x1823, size: 5
								this.parent.CommonTools.PlayTune(1, 0);

								if (wonderID == 14)
								{
									// Instruction address 0x1238:0x1837, size: 5
									this.parent.GameEngine.F0_1ade_1584_SelectNextResearchField(playerID, 0);

									// Instruction address 0x1238:0x1845, size: 5
									this.parent.GameEngine.F0_1ade_1584_SelectNextResearchField(playerID, 0);
								}

								for (int j = 0; j < 128; j++)
								{
									if (this.parent.GameData.Cities[j].Status != CityStatusEnum.Invalid &&
										this.parent.GameData.Cities[j].PlayerID == this.parent.GameData.HumanPlayerID &&
										this.parent.GameData.Cities[j].CurrentProductionID == -(wonderID + 24))
									{
										// Instruction address 0x1238:0x1893, size: 5
										this.parent.GameEngine.F0_1ade_03ea_ProcessCityState1AndRefreshMap(j);
									}
								}

								break;
							}
						}
					}
				}
			}
		}

		/// <summary>
		/// Fades main screen to black, draws the map and fades colors from black to main palette colors
		/// </summary>
		public void F0_1238_1b44_RedrawMapWithBlankingEffect()
		{
			//this.oCPU.Log.EnterBlock("F0_1238_1b44()");

			// function body
			// Instruction address 0x1238:0x1b48, size: 5
			this.parent.CommonTools.StopPaletteCycleSlots();

			// Instruction address 0x1238:0x1b66, size: 5
			this.parent.CommonTools.TransformPaletteToColor(5, Color.FromRgb(0, 0, 0));

			// Instruction address 0x1238:0x1b7f, size: 5
			this.parent.MapManagement.F0_2aea_0008_DrawVisibleMap(this.parent.GameData.HumanPlayerID, this.parent.Var_d4cc_MapViewX, this.parent.Var_d75e_MapViewY);

			// Instruction address 0x1238:0x1b97, size: 3
			this.parent.DrawTools.F0_1238_1bb2_FillRectangleWithShadow(0, 97, 80, 103);

			// Instruction address 0x1238:0x1ba3, size: 3
			this.parent.CommonTools.LoadAndApplyDefaultColorPalette();

			// Instruction address 0x1238:0x1bac, size: 5
			this.parent.CommonTools.StartPaletteCycleSlots();
		}

		/// <summary>
		/// Calculates the approximate palace level to be built from score
		/// </summary>
		/// <param name="score"></param>
		public int F0_1238_1c98_GetPalaceLevelFromScore(int score)
		{
			// function body
			int level = 0;

			do
			{
				level += 5;
				score -= level;
			} while (score > 0);

			level /= 5;
			level--;

			return level;
		}

		/// <summary>
		/// Global warming causes Icecap melt event
		/// </summary>
		/// <param name="globalWarmingCount"></param>
		public void F7_0000_1be3_IcecapMeltEvent(short globalWarmingCount)
		{
			//this.CPU.Log.EnterBlock($"F7_0000_1be3({globalWarmingCount})");

			// function body
			this.parent.News.F21_0000_0000_ShowNews(-1, "Global temperature\nrises! Icecaps melt.\nSevere Drought.\n");

			for (int i = 0; i < 80; i++)
			{
				for (int j = 0; j < 50; j++)
				{
					// Instruction address 0x0000:0x1cbe, size: 5
					TerrainTypeEnum terrainType = this.parent.MapManagement.GetTerrainType(i, j);

					if (terrainType == TerrainTypeEnum.Desert || terrainType == TerrainTypeEnum.Plains || terrainType == TerrainTypeEnum.Grassland || terrainType == TerrainTypeEnum.Forest)
					{
						int waterCount = 0;

						for (int k = 1; k < 9; k++)
						{
							GPoint direction = this.parent.MoveDirections[k];
							int newX = i + direction.X;
							int newY = j + direction.Y;

							if (this.parent.MapManagement.ValidateMapCoordinates(newX, newY) && this.parent.MapManagement.GetTerrainType(newX, newY) == TerrainTypeEnum.Water)
							{
								waterCount++;
							}
						}

						if (7 - globalWarmingCount <= waterCount)
						{
							this.parent.GameData.MapVisibility[i, j] |= 1;

							if (terrainType != TerrainTypeEnum.Forest)
							{
								// Instruction address 0x0000:0x1c18, size: 5
								this.parent.MapManagement.SetTerrainType(i, j, TerrainTypeEnum.Swamp);
							}
							else
							{
								// Instruction address 0x0000:0x1c18, size: 5
								this.parent.MapManagement.SetTerrainType(i, j, TerrainTypeEnum.Jungle);
							}

							// Instruction address 0x0000:0x1c2a, size: 5
							this.parent.MapManagement.F0_2aea_16ee_ClearTerrainImprovements(i, j, TerrainImprovementFlagsEnum.Mines | TerrainImprovementFlagsEnum.Irrigation);

							if ((this.parent.GameData.MapVisibility[i, j] & (0x1 << this.parent.GameData.HumanPlayerID)) != 0)
							{
								// Instruction address 0x0000:0x1ca4, size: 5
								this.parent.MapManagement.F0_2aea_11d4_DrawCellWithUnit(i, j);
							}
						}
						else if (((i * 11 + j * 13) & 0x7) == globalWarmingCount)
						{
							this.parent.GameData.MapVisibility[i, j] |= 1;

							if (terrainType == TerrainTypeEnum.Desert || terrainType == TerrainTypeEnum.Plains)
							{
								// Instruction address 0x0000:0x1c82, size: 5
								this.parent.MapManagement.SetTerrainType(i, j, TerrainTypeEnum.Desert);
							}
							else
							{
								// Instruction address 0x0000:0x1c82, size: 5
								this.parent.MapManagement.SetTerrainType(i, j, TerrainTypeEnum.Plains);
							}

							if ((this.parent.GameData.MapVisibility[i, j] & (0x1 << this.parent.GameData.HumanPlayerID)) != 0)
							{
								// Instruction address 0x0000:0x1ca4, size: 5
								this.parent.MapManagement.F0_2aea_11d4_DrawCellWithUnit(i, j);
							}
						}
					}
				}
			}
		}

		/// <summary>
		/// Get a sum of total population count for a given player
		/// </summary>
		/// <param name="playerID"></param>
		/// <returns></returns>
		public int GetPlayerPopulationCount(int playerID)
		{
			//this.oCPU.Log.EnterBlock($"F0_2dc4_02cd({playerID})");

			// function body
			int population = 0;

			for (int i = 0; i < 128; i++)
			{
				if (this.parent.GameData.Cities[i].PlayerID == playerID && this.parent.GameData.Cities[i].Status != CityStatusEnum.Invalid)
				{
					for (int j = 1; j <= this.parent.GameData.Cities[i].ActualSize; j++)
					{
						population += j;
					}
				}
			}

			return population;
		}

		/// <summary>
		/// Converts population value to string
		/// </summary>
		/// <param name="value"></param>
		public string PopulationValueToString(int value)
		{
			// function body
			return $"{(value * 1000):###,###,##0}";
		}

		/// <summary>
		/// Process cities and adjust tax rates
		/// </summary>
		/// <param name="playerID"></param>
		public void F0_1ade_0006_ProcessCitiesAndAdjustTaxRates(int playerID)
		{
			//this.oCPU.Log.EnterBlock($"F0_1ade_0006({playerID})");

			// function body			
			this.parent.Var_db42 = 0;
			this.parent.Var_e3c2 = 0;
			bool largeCityPresent = false;
			bool cityDisorderPresent = false;

			for (int i = 0; i < 128; i++)
			{
				if (this.parent.GameData.Cities[i].Status == CityStatusEnum.Invalid || this.parent.GameData.Cities[i].PlayerID == playerID)
				{
					do
					{
						// Instruction address 0x1ade:0x0051, size: 5
						if (this.parent.CityWorker.F0_1d12_0045_ProcessCityState(i, 0) == 0 && this.parent.GameData.Cities[i].ActualSize > 5)
						{
							largeCityPresent = true;
						}

						if ((this.parent.GameData.Cities[i].Status & CityStatusEnum.CivilDisorder) != CityStatusEnum.None)
						{
							cityDisorderPresent = true;
						}

						if (this.parent.Var_b1e8 != 0)
						{
							// Instruction address 0x1ade:0x0093, size: 5
							this.parent.CityWorker.F0_1d12_0045_ProcessCityState(i, 1);

							// Instruction address 0x1ade:0x009b, size: 5
							this.parent.GameEngine.F0_1238_1b44_RedrawMapWithBlankingEffect();

							if (this.parent.Var_b1e8 == 2)
							{
								this.parent.Var_b1e8 = 0;
								continue;
							}
						}
						break;
					}
					while (true);

					this.parent.Var_b1e8 = 0;
				}
			}

			if (playerID != this.parent.GameData.HumanPlayerID && (this.parent.GameData.DebugFlags & 0x4) != 0)
			{
				int totalTaxRate = this.parent.GameData.Players[playerID].ScienceTaxRate + this.parent.GameData.Players[playerID].TaxRate;
				int remainingTax = 10 - totalTaxRate;

				if (cityDisorderPresent && totalTaxRate > 6)
				{
					remainingTax++;
				}

				if ((this.parent.GameData.TurnCount & 0x3) == 0 && !largeCityPresent && !cityDisorderPresent && totalTaxRate < 8)
				{
					remainingTax--;
				}

				// Instruction address 0x1ade:0x0116, size: 5
				remainingTax = this.parent.CommonTools.CheckValueRange(remainingTax, 0, 4);

				if (this.parent.GameData.Players[playerID].NationalityID != -1)
				{
					this.parent.GameData.Players[playerID].ScienceTaxRate =
						(short)(this.parent.GameData.Nations[this.parent.GameData.Players[playerID].NationalityID].Ideology + (10 - remainingTax) / 2);
				}
				else
				{
					this.parent.GameData.Players[playerID].ScienceTaxRate = (short)((10 - remainingTax) / 2);
				}

				if (this.parent.GameData.Players[playerID].Coins > this.parent.GameData.TurnCount + 100)
				{
					this.parent.GameData.Players[playerID].ScienceTaxRate++;
				}

				if (F0_1ade_22b5_PlayerHasTechnology(playerID, TechnologyAdvanceEnum.Robotics))
				{
					this.parent.GameData.Players[playerID].ScienceTaxRate = 0;
				}

				this.parent.GameData.Players[playerID].TaxRate = (short)(10 - (this.parent.GameData.Players[playerID].ScienceTaxRate + remainingTax));
			}
		}

		/// <summary>
		/// Deletes the city
		/// </summary>
		/// <param name="cityID"></param>
		/// <param name="x"></param>
		/// <param name="y"></param>
		public void F0_1ade_018e_DeleteCity(int cityID, int x, int y)
		{
			//this.CPU.Log.EnterBlock($"F0_1ade_018e({cityID}, {x}, {y})");

			// function body
			int playerID = this.parent.GameData.Cities[cityID].PlayerID;

			// Instruction address 0x1ade:0x01b7, size: 5
			this.parent.UnitManagement.F0_1866_250e_AddReplayData(1, 0xff, (byte)this.parent.GameData.Cities[cityID].NameID, (byte)((sbyte)x), (byte)((sbyte)y));

			if (this.parent.GameData.Cities[cityID].CurrentProductionID >= 0)
			{
				this.parent.GameData.Players[playerID].UnitsInProduction[this.parent.GameData.Cities[cityID].CurrentProductionID]--;
			}

			this.parent.GameData.Players[playerID].CityCount--;
			this.parent.GameData.Cities[cityID].Status = CityStatusEnum.Invalid;

			// Instruction address 0x1ade:0x01fe, size: 5
			this.parent.MapManagement.F0_2aea_1653_SetTerrainImprovements(x, y, TerrainImprovementFlagsEnum.None);

			if ((this.parent.GameData.MapVisibility[x, y] & (0x1 << this.parent.GameData.HumanPlayerID)) != 0)
			{
				// Instruction address 0x1ade:0x022e, size: 5
				this.parent.MapManagement.F0_2aea_1601_UpdateVisibleCellStatus(x, y);
			}

			for (int i = 0; i < 128; i++)
			{
				for (int j = 0; j < 3; j++)
				{
					if (cityID == this.parent.GameData.Cities[i].TradeCityIDs[j])
					{
						this.parent.GameData.Cities[i].TradeCityIDs[j] = -1;
					}
				}
			}

			for (int i = 1; i < 22; i++)
			{
				if (cityID == this.parent.GameData.WonderCityID[i])
				{
					this.parent.GameData.WonderCityID[i] = 128;
				}
			}

			for (int i = 0; i < 128; i++)
			{
				if (this.parent.GameData.Players[playerID].Units[i].UnitType != UnitTypeEnum.None)
				{
					if (this.parent.GameData.Players[playerID].Units[i].HomeCityID == cityID)
					{
						// Instruction address 0x1ade:0x02ce, size: 5
						this.parent.UnitManagement.F0_1866_0f10_DeleteUnit(playerID, i);
					}
				}
			}

			// Instruction address 0x1ade:0x02e6, size: 5
			int activeUnitID = this.parent.MapManagement.F0_2aea_1458_GetCellActiveUnitID(x, y);

			if (activeUnitID != -1)
			{
				// Instruction address 0x1ade:0x030f, size: 5
				this.parent.UnitManagement.F0_1866_1643_DeleteNonLandUnitsInStack(playerID, activeUnitID);
			}

			for (int i = 0; i < 49; i++)
			{
				GPoint direction = this.parent.MoveDirections[i];

				// Instruction address 0x1ade:0x0329, size: 5
				int newX = this.parent.MapManagement.AdjustXPosition(x + direction.X);
				int newY = y + direction.Y;

				if (this.parent.MapManagement.ValidateMapCoordinates(newX, newY))
				{
					// Instruction address 0x1ade:0x0342, size: 5
					int nearestCity = this.parent.MapManagement.FindNearestCity(newX, newY);
					int nearestCityDistance = (nearestCity >= 0) ? this.parent.MapManagement.GetDistance(newX, newY, this.parent.GameData.Cities[nearestCity].Position) : int.MaxValue;

					if (i < 21 || nearestCityDistance > 5)
					{
						// Instruction address 0x1ade:0x037d, size: 5
						this.parent.MapManagement.SetPlayerLandOwnership(newX, newY, this.parent.MapManagement.GetBuildLocationScore(newX, newY));
					}
				}
			}
		}

		/// <summary>
		/// Process city state (1) and refresh the map
		/// </summary>
		/// <param name="cityID"></param>
		public void F0_1ade_03ea_ProcessCityState1AndRefreshMap(int cityID)
		{
			//this.oCPU.Log.EnterBlock($"F0_1ade_03ea({cityID})");

			// function body
			// Instruction address 0x1ade:0x03f1, size: 3
			this.parent.Graphics.F0_VGA_07d8_DrawImage(this.parent.Var_aa_Screen0_Rectangle, 0, 0, 320, 200, this.parent.Var_19d4_Screen1_Rectangle, 0, 0);

			this.parent.Var_6b64 = 0;

			// Instruction address 0x1ade:0x0403, size: 5
			this.parent.CityWorker.F0_1d12_0045_ProcessCityState(cityID, 1);

			if (this.parent.Var_6b64 != 0)
			{
				// Instruction address 0x1ade:0x0412, size: 5
				this.parent.GameEngine.F0_1238_1b44_RedrawMapWithBlankingEffect();
			}
			else
			{
				// Instruction address 0x1ade:0x041a, size: 3
				this.parent.Graphics.F0_VGA_07d8_DrawImage(this.parent.Var_19d4_Screen1_Rectangle, 0, 0, 320, 200, this.parent.Var_aa_Screen0_Rectangle, 0, 0);
			}
		}

		/// <summary>
		/// Selects city production (!!! needs further refinement)
		/// </summary>
		/// <param name="playerID"></param>
		/// <param name="cityID"></param>
		/// <returns></returns>
		public int F0_1ade_0421_SelectCityProduction(int playerID, int cityID)
		{
			//this.CPU.Log.EnterBlock($"F0_1ade_0421({playerID}, {cityID})");

			// function body
			bool firstPass = true;
			int[] playerUnitBuildCount = new int[28];
			int[] buildIDList = new int[76];
			int[] currentUnitBuildCount = new int[28];
			int buildListSize = 0;
			UnitStrategyTypeEnum continentStrategy;
			int buildSelectionIndex = 0;

			// Instruction address 0x1ade:0x043f, size: 5
			this.parent.CityWorker.F0_1d12_0045_ProcessCityState(cityID, -1);

			while (true)
			{
				int buildListCount = 1;
				buildIDList[0] = this.parent.GameData.Cities[cityID].CurrentProductionID;
				StringBuilder messageText = new();

				// Instruction address 0x1ade:0x0465, size: 5
				messageText.Append($"What shall we build in {this.parent.Segment_2459.F0_2459_08c6_GetCityName(cityID)}?\n ");

				if (buildListSize != 2)
				{
					for (int i = 0; i < 28; i++)
					{
						if (i != 25 ||
							(this.parent.GameData.WonderCityID[17] != -1 && F0_1ade_22b5_PlayerHasTechnology(playerID, TechnologyAdvanceEnum.NuclearFission)))
						{
							if (F0_1ade_22b5_PlayerHasTechnology(playerID, this.parent.GameData.Units[i].RequiredTechnology) &&
								!F0_1ade_22b5_PlayerHasTechnology(playerID, this.parent.GameData.Units[i].CancelTechnology))
							{
								if (this.parent.GameData.Units[i].MovementType == UnitMovementTypeEnum.Water && 
									(this.parent.GameData.Cities[cityID].Status & CityStatusEnum.WaterAccess) != CityStatusEnum.None)
								{
									// Instruction address 0x1ade:0x04f8, size: 5
									messageText.Append($"{this.parent.GameData.Units[i].Name} " +
										$"{F0_1ade_14ed_CostToTurns(cityID, this.parent.GameData.Units[i].Cost)}, " +
										$"ADM: {this.parent.GameData.Units[i].AttackStrength}/{this.parent.GameData.Units[i].DefenseStrength}/" +
										$"{this.parent.GameData.Units[i].MoveCount})\n ");

									buildIDList[buildListCount] = i;

									buildListCount++;
								}
							}
						}
					}
				}

				int currentBuildListCount = buildListCount;

				// Instruction address 0x1ade:0x05ff, size: 5
				StringBuilder improvementTextList = new();

				bool hooverDamPresent = false;

				// Instruction address 0x1ade:0x0617, size: 5
				if (this.parent.CityWorker.F0_1d12_6c97_PlayerHasWonder(playerID, WonderEnum.HooverDam))
				{
					// Instruction address 0x1ade:0x0658, size: 5
					if (this.parent.MapManagement.F0_2aea_1942_GetGroupID(this.parent.GameData.Cities[cityID].Position.X, this.parent.GameData.Cities[cityID].Position.Y) ==
						this.parent.MapManagement.F0_2aea_1942_GetGroupID(this.parent.GameData.Cities[this.parent.GameData.WonderCityID[15]].Position.X,
							this.parent.GameData.Cities[this.parent.GameData.WonderCityID[15]].Position.Y))
					{
						hooverDamPresent = true;
					}
				}

				if ((this.parent.GameData.DebugFlags & 0x4) != 0)
				{
					for (int i = 1; i < 25; i++)
					{
						// Instruction address 0x1ade:0x06be, size: 3
						if (F0_1ade_22b5_PlayerHasTechnology(playerID, this.parent.GameData.GetImprovementType(i).RequiresTechnology) &&
							(this.parent.GameData.Cities[cityID].ImprovementFlags & (0x1 << (i - 1))) == 0)
						{
							if (i != 7 || (this.parent.GameData.Cities[cityID].ImprovementFlags & 0x1) == 0)
							{
								if (i != 15 || (this.parent.GameData.Cities[cityID].ImprovementFlags & 0x8000) == 0)
								{
									if (i != 10 || (this.parent.GameData.Cities[cityID].ImprovementFlags & 0x10) != 0 ||
										!F0_1ade_22b5_PlayerHasTechnology(playerID, this.parent.GameData.GetImprovementType((int)ImprovementEnum.MarketPlace).RequiresTechnology))
									{
										if (i != 12 || (this.parent.GameData.Cities[cityID].ImprovementFlags & 0x20) != 0 ||
											!F0_1ade_22b5_PlayerHasTechnology(playerID, this.parent.GameData.GetImprovementType((int)ImprovementEnum.Library).RequiresTechnology))
										{
											if (i != 20 || (this.parent.GameData.Cities[cityID].Status & CityStatusEnum.NearRiverOrMountains) != CityStatusEnum.None)
											{
												if (i != 19 || (this.parent.GameData.Cities[cityID].ImprovementFlags & 0x4000) != 0)
												{
													bool flag = false;

													if (i == 19 || i == 20 || i == 21)
													{
														if (!hooverDamPresent && (this.parent.GameData.Cities[cityID].ImprovementFlags & 0x1c0000) == 0)
														{
															flag = true;
														}
													}
													else
													{
														flag = true;
													}

													if (flag)
													{
														if (i <= 21 || (this.parent.GameData.WonderCityID[19] != -1 && (this.parent.GameData.SpaceshipFlags & (0x1 << playerID)) == 0))
														{
															// Instruction address 0x1ade:0x07f4, size: 5
															improvementTextList.Append($"{this.parent.GameData.GetImprovementType(i).Name} " +
																$"{F0_1ade_14ed_CostToTurns(cityID, this.parent.GameData.GetImprovementType(i).Cost)})\n ");

															buildIDList[buildListCount] = -i;

															buildListCount++;
														}
													}
												}
											}
										}
									}
								}
							}
						}
					}
				}

				for (int i = 25; i < 46; i++)
				{
					// Instruction address 0x1ade:0x0846, size: 3
					if (F0_1ade_22b5_PlayerHasTechnology(playerID, this.parent.GameData.GetImprovementType(i).RequiresTechnology) &&
						this.parent.GameData.WonderCityID[i - 24] == -1)
					{
						if (this.parent.GameData.GetImprovementType(i).ObsoletesAfterTechnology != TechnologyAdvanceEnum.None &&
							(this.parent.GameData.TechnologyFirstDiscoveredBy[(int)this.parent.GameData.GetImprovementType(i).ObsoletesAfterTechnology] & 7) != 0)
						{
							// Instruction address 0x1ade:0x087e, size: 5
							improvementTextList.Append("*");
						}

						// Instruction address 0x1ade:0x0897, size: 5
						improvementTextList.Append($"{this.parent.GameData.GetImprovementType(i).Name} " +
							$"{F0_1ade_14ed_CostToTurns(cityID, this.parent.GameData.GetImprovementType(i).Cost)})\n ");

						buildIDList[buildListCount] = -i;

						buildListCount++;
					}
				}

				if (playerID == this.parent.GameData.HumanPlayerID &&
					(this.parent.GameData.Cities[cityID].Status & CityStatusEnum.AutoProduction) == CityStatusEnum.None && buildListCount > 30)
				{
					if (buildListSize != 0)
					{
						improvementTextList.Clear();
						messageText.Append("more...\n");

						buildIDList[currentBuildListCount] = 99;
					}
					else
					{
						messageText.Append(improvementTextList.ToString());
						buildListSize = 1;
						continue;
					}
				}

				messageText.Append(improvementTextList.ToString());

				if (buildListSize == 2)
				{
					// Instruction address 0x1ade:0x0939, size: 5
					messageText.Append("more...\n");

					buildIDList[buildListCount] = 98;

					buildListCount++;
				}

				int local_2;
				int local_13c;

				// Instruction address 0x1ade:0x0963, size: 5
				int local_13e = this.parent.MapManagement.F0_2aea_1942_GetGroupID(this.parent.GameData.Cities[cityID].Position.X, this.parent.GameData.Cities[cityID].Position.Y);
				continentStrategy = this.parent.GameData.Players[playerID].Continents[local_13e].Strategy;

				if (continentStrategy == UnitStrategyTypeEnum.LandAttack || continentStrategy == UnitStrategyTypeEnum.Defense)
				{
					local_13c = 1;
				}
				else
				{
					local_13c = 0;
				}

				if (this.parent.Var_8078 != 0 && continentStrategy == UnitStrategyTypeEnum.SeaTransport)
				{
					local_13c = 1;
				}

				if (this.parent.GameData.Players[playerID].GovernmentType != GovernmentTypeEnum.Anarchy && 
					this.parent.GameData.Players[playerID].GovernmentType != GovernmentTypeEnum.Despotism)
				{
					local_2 = this.parent.Var_70da_Arr[0] - this.parent.Var_e3c6 * 2 - this.parent.GameData.Cities[cityID].ActualSize * 2;
				}
				else
				{
					local_2 = this.parent.Var_70da_Arr[0] - this.parent.Var_e3c6 - this.parent.GameData.Cities[cityID].ActualSize * 2;
				}

				for (int i = 0; i < 28; i++)
				{
					currentUnitBuildCount[i] = 1;
					playerUnitBuildCount[i] = 0;
				}

				int currentUnitCount = this.parent.GameData.Players[playerID].UnitCount;
				this.parent.GameData.Players[playerID].UnitCount = 0;

				for (int i = 0; i < 128; i++)
				{
					if (this.parent.GameData.Cities[i].Status != CityStatusEnum.Invalid && this.parent.GameData.Cities[i].PlayerID == playerID &&
						this.parent.MapManagement.F0_2aea_1942_GetGroupID(this.parent.GameData.Cities[i].Position.X, this.parent.GameData.Cities[i].Position.Y) == local_13e)
					{
						if (this.parent.GameData.Cities[i].CurrentProductionID >= 0 && i != cityID)
						{
							currentUnitBuildCount[this.parent.GameData.Cities[i].CurrentProductionID]++;
						}

						for (int local_11c = 0; local_11c < 2; local_11c++)
						{
							if (this.parent.GameData.Cities[i].FirstFortifiedCityUnits[local_11c] != -1)
							{
								this.parent.GameData.Players[playerID].UnitCount++;
								playerUnitBuildCount[(this.parent.GameData.Cities[i].FirstFortifiedCityUnits[local_11c] & 0x3f)]++;
							}
						}
					}
				}

				for (int i = 0; i < 128; i++)
				{
					if (this.parent.GameData.Players[playerID].Units[i].UnitType != UnitTypeEnum.None)
					{
						this.parent.GameData.Players[playerID].UnitCount++;

						// Instruction address 0x1ade:0x0ae5, size: 5
						if (this.parent.MapManagement.F0_2aea_1942_GetGroupID(this.parent.GameData.Players[playerID].Units[i].Position.X,
							this.parent.GameData.Players[playerID].Units[i].Position.Y) == local_13e)
						{
							playerUnitBuildCount[(int)this.parent.GameData.Players[playerID].Units[i].UnitType]++;
						}
					}
				}

				if (this.parent.GameData.Players[playerID].UnitCount == 0 && playerID != this.parent.GameData.HumanPlayerID &&
					(this.parent.GameData.Players[playerID].GovernmentType == GovernmentTypeEnum.Anarchy ||
					this.parent.GameData.Players[playerID].GovernmentType == GovernmentTypeEnum.Despotism))
				{
					return 1;
				}

				if (playerID == 0)
				{
					if ((this.parent.GameData.TechnologyFirstDiscoveredBy[(int)TechnologyAdvanceEnum.Explosives] & 0x7) == 0)
					{
						return 3;
					}

					return 7;
				}

				int local_c;
				int local_48;
				int local_4c;
				int local_11e;
				int local_130;
				int local_132;
				int local_134;

				int local_8 = 0;

				// Instruction address 0x1ade:0x0b63, size: 5
				int activeUnitID = this.parent.MapManagement.F0_2aea_1458_GetCellActiveUnitID(this.parent.GameData.Cities[cityID].Position.X, this.parent.GameData.Cities[cityID].Position.Y);

				if (activeUnitID == -1)
				{
					local_8 = 2;
				}
				else
				{
					// Instruction address 0x1ade:0x0b86, size: 5			
					if (this.parent.GameData.Cities[cityID].ActualSize / 4 + local_13c > this.parent.UnitManagement.F0_1866_1380_GetStackUnitCount(playerID, activeUnitID, UnitStrategyTypeEnum.Defense))
					{
						local_8 = 1;
					}
				}

				local_11e = 999;
				local_130 = 999;
				local_4c = 999;
				local_c = -1;
				local_48 = -1;

				for (int i = 1; i < buildListCount; i++)
				{
					int buidID = buildIDList[i];

					if (buidID != 23)
					{
						if (buidID >= 0 && buidID < 26)
						{
							if (buidID != 0 || ((this.parent.GameData.Players[playerID].GovernmentType == GovernmentTypeEnum.Anarchy ||
								this.parent.GameData.Players[playerID].GovernmentType == GovernmentTypeEnum.Despotism) ? 1 : 2) < local_2 ||
								(this.parent.GameData.Players[playerID].ActiveUnits[(int)UnitTypeEnum.Settler] == 0 && currentUnitBuildCount[0] <= 1))
							{
								if (buidID != 1 || this.parent.GameData.Players[playerID].GovernmentType == GovernmentTypeEnum.Anarchy ||
									this.parent.GameData.Players[playerID].GovernmentType == GovernmentTypeEnum.Despotism)
								{
									if (buidID != 25 || (this.parent.GameData.Players[playerID].ActiveUnits[(int)UnitTypeEnum.Nuclear] < 4 &&
										this.parent.GameData.Players[playerID].UnitsInProduction[(int)UnitTypeEnum.Nuclear] < 2))
									{
										if (buidID != 14 || (this.parent.GameData.Players[playerID].ActiveUnits[buidID] + this.parent.GameData.Players[playerID].UnitsInProduction[buidID] <
											this.parent.GameData.Players[this.parent.GameData.HumanPlayerID].ActiveUnits[(int)UnitTypeEnum.Bomber] / 2 &&
											!this.parent.GameData.Players[playerID].Diplomacy[this.parent.GameData.HumanPlayerID].HasFlag(DiplomacyFlagsEnum.Peace)))
										{
											if (this.parent.GameData.Units[buidID].MovementType == UnitMovementTypeEnum.Land ||
												this.parent.GameData.Players[playerID].Continents[local_13e].Attack >= 8)
											{
												if (this.parent.GameData.Units[buidID].MovementType != UnitMovementTypeEnum.Land)
												{
													local_134 = (this.parent.GameData.Players[playerID].Continents[local_13e].Attack *
														(this.parent.GameData.Players[playerID].UnitsInProduction[buidID] * 2 + this.parent.GameData.Players[playerID].ActiveUnits[buidID])) /
														(this.parent.GameData.Players[playerID].UnitCount + 1);
												}
												else
												{
													local_134 = currentUnitBuildCount[buidID] * 2 + playerUnitBuildCount[buidID];
												}

												if (local_8 == 0 || this.parent.GameData.Units[buidID].UnitRoleType == UnitStrategyTypeEnum.Defense)
												{
													if (local_8 != 0)
													{
														local_134 = 2 / local_8;
													}

													if (this.parent.GameData.Units[buidID].UnitRoleType != UnitStrategyTypeEnum.Defense &&
														this.parent.GameData.Units[buidID].UnitRoleType != UnitStrategyTypeEnum.Settle)
													{
														local_134 *= 2;

														if (continentStrategy != this.parent.GameData.Units[buidID].UnitRoleType)
														{
															local_134 *= 2;
														}
													}

													if (this.parent.GameData.Units[buidID].UnitRoleType != UnitStrategyTypeEnum.SeaTransport || continentStrategy == UnitStrategyTypeEnum.SeaTransport)
													{
														if (continentStrategy == UnitStrategyTypeEnum.SeaTransport)
														{
															local_134 = (3 * local_134) / 2;
														}

														if (buidID == 0)
														{
															if ((this.parent.GameData.Cities[cityID].ImprovementFlags0 & 0x4) != 0)
															{
																local_134 = (4 - this.parent.GameData.Nations[this.parent.GameData.Players[playerID].NationalityID].Ideology) *
																	(this.parent.GameData.Units[buidID].Cost + 2) * local_134;
																local_134 /= (this.parent.GameData.Units[buidID].AttackStrength + this.parent.GameData.Units[buidID].DefenseStrength) *
																	(this.parent.GameData.Units[buidID].MoveCount + 1);
															}
															else
															{
																local_134 = (6 - this.parent.GameData.Nations[this.parent.GameData.Players[playerID].NationalityID].Ideology) *
																	(this.parent.GameData.Units[buidID].Cost + 2) * local_134;
																local_134 /= (this.parent.GameData.Units[buidID].AttackStrength + this.parent.GameData.Units[buidID].DefenseStrength) *
																	(this.parent.GameData.Units[buidID].MoveCount + 1);
															}
														}
														else
														{
															local_134 = 16 / ((this.parent.GameData.Units[buidID].AttackStrength + this.parent.GameData.Units[buidID].DefenseStrength) *
																(this.parent.GameData.Units[buidID].MoveCount + 1));
														}

														if (this.parent.GameData.Units[buidID].UnitRoleType == UnitStrategyTypeEnum.SeaAttack)
														{
															if (local_13c != 0)
															{
																local_134 *= 4;
															}
															else
															{
																local_134 *= 2;
															}
														}

														if (this.parent.GameData.Units[buidID].MovementType == UnitMovementTypeEnum.Air)
														{
															local_134 *= 2;
														}

														if (this.parent.GameData.Players[playerID].UnitCount > 120)
														{
															local_134 *= 4;
														}

														if ((local_13c == 0 && this.parent.Var_70da_Arr[1] <= this.parent.Var_deb8) || !this.parent.AIEngine.MakeContactWithPlayer)
														{
															local_134 *= 2;
														}

														if (local_134 < local_4c)
														{
															buildSelectionIndex = i;
															local_4c = local_134;
														}

														if (buidID < 0 || buidID >= 26)
														{
															if (local_134 < local_130)
															{
																local_130 = local_134;
																local_48 = i;
															}
														}
														else if (local_134 < local_11e)
														{
															local_11e = local_134;
															local_c = i;
														}
													}
												}
											}
										}
									}
								}
							}
						}
						else
						{
							local_134 = 999;
							local_132 = 0;
							buidID = 27 - buidID;

							switch (buidID)
							{
								case 0:
									if (this.parent.GameData.Cities[cityID].TradeCityIDs[2] == -1)
									{
										local_134 = 10 - this.parent.Var_70da_Arr[2] / 2;

										if (this.parent.GameData.Cities[cityID].TradeCityIDs[0] != -1)
										{
											local_134 += 2;
										}

										if (this.parent.GameData.Cities[cityID].TradeCityIDs[1] != -1)
										{
											local_134 += 2;
										}

										if (local_134 < 3)
										{
											local_134 = 3;
										}
									}

									if (playerID == this.parent.GameData.HumanPlayerID && (this.parent.GameData.Cities[cityID].Status & CityStatusEnum.AutoProduction) != CityStatusEnum.None)
									{
										local_134 = 999;
									}
									break;

								case 1:
									if (this.parent.GameData.HumanPlayerID > 0 &&
										!this.parent.GameData.Players[playerID].Diplomacy[this.parent.GameData.HumanPlayerID].HasFlag(DiplomacyFlagsEnum.Peace) &&
										this.parent.GameData.Players[playerID].UnitsInProduction[(int)UnitTypeEnum.Caravan] <= 0)
									{
										int local_4a = this.parent.CAPI.RNG.Next(5);

										if ((this.parent.AIEngine.AIPlayerFlags & (0x1 << playerID)) != 0 &&
											(this.parent.GameData.Players[this.parent.GameData.HumanPlayerID].DiscoveredTechnologyFlags[local_4a] |
												this.parent.GameData.Players[playerID].DiscoveredTechnologyFlags[local_4a]) != this.parent.GameData.Players[playerID].DiscoveredTechnologyFlags[local_4a])
										{
											// Instruction address 0x1ade:0x0e66, size: 5
											local_134 = this.parent.CommonTools.CheckValueRange(this.parent.GameData.Players[playerID].DiscoveredTechnologyCount -
												this.parent.GameData.Players[this.parent.GameData.HumanPlayerID].DiscoveredTechnologyCount + 10, 5, 10);
										}
									}
									break;

								case 29:
									local_134 = 5;
									local_132 = 1;
									break;

								case 30:
									if (this.parent.Var_70da_Arr[1] < 3)
									{
										local_134 = 8;
									}
									else
									{
										local_134 = 4;
									}
									break;

								case 31:
									if ((this.parent.GameData.Cities[cityID].Status & CityStatusEnum.CivilDisorder) != CityStatusEnum.None)
									{
										local_134 = -4;
									}
									break;

								case 32:
									local_134 = 10 - this.parent.Var_70da_Arr[2] / 2;

									if ((this.parent.GameData.Cities[cityID].Status & CityStatusEnum.CivilDisorder) != CityStatusEnum.None)
									{
										local_134 = -3;
									}
									break;

								case 33:
								case 37:
									local_134 = 10 - this.parent.Var_70da_Arr[2] / 3;
									break;

								case 34:
									local_134 = 12 - this.parent.Var_d2e0 * 2;
									break;

								case 35:
									local_134 = 10 - this.parent.GameData.Cities[cityID].ActualSize / 2;

									if ((this.parent.GameData.Cities[cityID].ImprovementFlags & 0x1) != 0)
									{
										local_134 -= 4;
										local_132 = 1;
									}
									else
									{
										local_132 = 1;
									}
									break;

								case 36:
									if (10 - local_2 / 2 <= this.parent.GameData.Cities[cityID].ActualSize)
									{
										local_134 = 14 - this.parent.GameData.Cities[cityID].ActualSize + local_2 / 2;
									}
									break;

								case 38:
									if ((this.parent.GameData.Cities[cityID].Status & CityStatusEnum.CivilDisorder) != CityStatusEnum.None)
									{
										local_134 = -2;
									}
									break;

								case 39:
									local_134 = 10 - this.parent.Var_70da_Arr[2] / 4;
									break;

								case 41:
									if ((this.parent.GameData.Cities[cityID].Status & CityStatusEnum.CivilDisorder) != CityStatusEnum.None)
									{
										local_134 = -1;
									}
									break;

								case 42:
								case 47:
									local_134 = 13 - this.parent.Var_70da_Arr[1] / 2;
									break;

								case 43:
								case 45:
								case 48:
									local_134 = 12 - this.parent.Var_70da_Arr[1] / 8;
									break;

								case 46:
									local_134 = 12 - this.parent.Var_70da_Arr[1] / 5;
									break;

								case 49:
									if ((this.parent.GameData.SpaceshipFlags & ((0x1 << playerID) | (0x1 << this.parent.GameData.HumanPlayerID))) == 0)
									{
										if ((this.parent.GameData.SpaceshipFlags & (0x1 << (this.parent.GameData.HumanPlayerID + 8))) != 0)
										{
											local_134 = 2;
										}
										else
										{
											local_134 = 4;
										}
									}
									break;

								default:
									break;
							}

							if (local_134 < 400)
							{
								if (local_13c != local_132)
								{
									local_134 = local_134 * 20;
								}
								else
								{
									local_134 = local_134 * 10;
								}

								local_134 = (local_134 * 3) / (this.parent.GameData.Nations[this.parent.GameData.Players[playerID].NationalityID].Ideology + 3);
							}
							else
							{
								local_134 = 16383;
							}

							if ((this.parent.GameData.Cities[cityID].ImprovementFlags & 0x2) != 0)
							{
								local_134 += local_134 / 4;
							}

							if (local_134 < local_4c)
							{
								buildSelectionIndex = i;
								local_4c = local_134;
							}

							if (buidID < 0 || buidID >= 26)
							{
								if (local_134 < local_130)
								{
									local_130 = local_134;
									local_48 = i;
								}
							}
							else if (local_134 < local_11e)
							{
								local_11e = local_134;
								local_c = i;
							}
						}
					}
				}

				this.parent.GameData.Players[playerID].UnitCount = (short)currentUnitCount;

				if ((this.parent.GameData.PlayerFlags & (0x1 << playerID)) != 0)
				{
					if ((this.parent.GameData.Cities[cityID].Status & CityStatusEnum.AutoProduction) == CityStatusEnum.None)
					{
						if (this.parent.GameData.GameSettingFlags.InstantAdvice)
						{
							if (firstPass)
							{
								this.parent.Var_aa_Screen0_Rectangle.FontID = 2;

								if (local_c != -1)
								{
									local_134 = buildIDList[local_c];

									if (local_134 >= 0)
									{
										// Instruction address 0x1ade:0x1323, size: 5
										this.parent.Array_30b8[3] = this.parent.GameData.Units[local_134].Name;
									}
									else
									{
										// Instruction address 0x1ade:0x1323, size: 5
										this.parent.Array_30b8[3] = this.parent.GameData.GetImprovementType(-local_134).Name;
									}

									this.parent.Var_db38 = 1;

									// Instruction address 0x1ade:0x1380, size: 5
									this.parent.MenuBoxDialog.F0_2d05_0031_ShowMenuBox(
										$"Military Advisor:\n{this.parent.LanguageTools.F0_2f4d_0000_AdjustTextBlockWidth(
											this.parent.LanguageTools.F0_2f4d_0471_ReplaceKeywords(
											this.parent.LanguageTools.F0_2f4d_01ad_GetTextBySectionAndKey("PRODUCE", $"*{GameNames.OriginalName(this.parent.Array_30b8[3])}")), 50)}",
										8, 160, true, false, true);
								}

								if (local_48 != -1)
								{
									local_134 = buildIDList[local_48];

									if (local_134 >= 0)
									{
										// Instruction address 0x1ade:0x13c2, size: 5
										this.parent.Array_30b8[3] = this.parent.GameData.Units[local_134].Name;
									}
									else
									{
										// Instruction address 0x1ade:0x13c2, size: 5
										this.parent.Array_30b8[3] = this.parent.GameData.GetImprovementType(-local_134).Name;
									}

									this.parent.Var_db38 = 1;

									// Instruction address 0x1ade:0x141f, size: 5
									this.parent.MenuBoxDialog.F0_2d05_0031_ShowMenuBox(
										this.parent.LanguageTools.F0_2f4d_0000_AdjustTextBlockWidth(
										"Domestic Advisor:\n" + this.parent.LanguageTools.F0_2f4d_0471_ReplaceKeywords(this.parent.LanguageTools.F0_2f4d_01ad_GetTextBySectionAndKey("PRODUCE",
										$"*{GameNames.OriginalName(this.parent.Array_30b8[3])}")), 50), 164, 160, true, false, true);
								}

								firstPass = false;
								this.parent.Var_aa_Screen0_Rectangle.FontID = 1;

								continue;
							}
						}

						if (buildListCount > 20 || buildListSize != 0)
						{
							this.parent.Var_aa_Screen0_Rectangle.FontID = 2;
						}

						// Instruction address 0x1ade:0x1460, size: 5
						local_134 = buildIDList[this.parent.MenuBoxDialog.F0_2d05_0031_ShowMenuBox(messageText.ToString(), 80, 8, true, true, true) - 1];

						if (local_134 != 99)
						{
							if (local_134 != 98)
							{
								this.parent.Var_aa_Screen0_Rectangle.FontID = 1;

								if (!this.parent.Var_2f9c_MenuBoxHelpRequested)
								{
									return local_134;
								}

								this.parent.Graphics.F0_VGA_07d8_DrawImage(this.parent.Var_aa_Screen0_Rectangle, 0, 0, 320, 200, this.parent.Var_19d4_Screen1_Rectangle, 0, 0);

								this.parent.Var_3c62 = 1;

								if (local_134 >= 0)
								{
									this.parent.Encyclopedia.DisplayEncyclopediaTopic(EncyclopediaTopicEnum.UnitType, local_134);
								}
								else
								{
									this.parent.Encyclopedia.DisplayEncyclopediaTopic(EncyclopediaTopicEnum.CityImprovement, -local_134);
								}

								this.parent.Graphics.F0_VGA_07d8_DrawImage(this.parent.Var_19d4_Screen1_Rectangle, 0, 0, 320, 200, this.parent.Var_aa_Screen0_Rectangle, 0, 0);

								this.parent.Var_6b64 = 1;
							}
							else
							{
								buildListSize = 1;
							}
						}
						else
						{
							buildListSize = 2;
						}
						continue;
					}

					if (local_48 == -1 || local_130 > 500)
					{
						return 99;
					}

					return buildIDList[local_48];
				}

				if (this.parent.GameData.Cities[cityID].CurrentProductionID != -1)
				{
					return buildIDList[buildSelectionIndex];
				}

				return -1;
			}
		}

		/// <summary>
		/// Convert cost to turns
		/// </summary>
		/// <param name="cityID"></param>
		/// <param name="cost"></param>
		public string F0_1ade_14ed_CostToTurns(int cityID, int cost)
		{
			//this.CPU.Log.EnterBlock($"F0_1ade_14ed({cityID}, {cost})");

			// function body
			// Instruction address 0x1ade:0x152d, size: 5
			int numberOfTurns = this.parent.CommonTools.CheckValueRange(
				(((10 * cost) - this.parent.GameData.Cities[cityID].ShieldsCount - 1) / this.parent.CommonTools.CheckValueRange(this.parent.Var_70da_Arr[1] -
					this.parent.Var_d2f6, 1, 99)) + 1, 1, 999);

			return $" ({numberOfTurns} turn{((numberOfTurns != 1) ? "s" : "")}";
		}

		/// <summary>
		/// Selects new research field
		/// </summary>
		/// <param name="playerID"></param>
		/// <param name="playerID1"></param>
		public void F0_1ade_1584_SelectNextResearchField(int playerID, int playerID1)
		{
			//this.CPU.Log.EnterBlock($"F0_1ade_1584({playerID}, {playerID1})");

			// function body
			if (playerID != 0)
			{
				int researchFieldCount = 0;
				bool firstPass = true;
				int suggestedResearchField = -1;
				int[] researchIDList = new int[17];
				int researchMaximumScore = -1;

				for (int i = (int)TechnologyAdvanceEnum.Alphabet; i < 72; i++)
				{
					if (!F0_1ade_22b5_PlayerHasTechnology(playerID, (TechnologyAdvanceEnum)i) &&
						F0_1ade_22b5_PlayerHasTechnology(playerID, this.parent.GameData.TechnologyAdvances[i].RequiresTechnologyAdvance1) &&
						F0_1ade_22b5_PlayerHasTechnology(playerID, this.parent.GameData.TechnologyAdvances[i].RequiresTechnologyAdvance2))
					{
						// Instruction address 0x1ade:0x15fd, size: 5
						int technologyScore = this.parent.CAPI.RNG.Next(F0_1ade_2317_GradeTechnology(playerID, (TechnologyAdvanceEnum)i) * 4);

						if (technologyScore > researchMaximumScore)
						{
							if (i != this.parent.GameData.Players[this.parent.GameData.HumanPlayerID].ResearchTechnologyID || playerID != this.parent.GameData.HumanPlayerID)
							{
								researchMaximumScore = technologyScore;
								suggestedResearchField = i;
							}
						}
					}
				}

				while (true)
				{
					if (playerID != this.parent.GameData.HumanPlayerID || this.parent.GameData.TurnCount == 0)
					{
						if (this.parent.GameData.TurnCount == 0 || this.parent.GameData.DifficultyLevel != 0 ||
							F0_1ade_22b5_PlayerHasTechnology(this.parent.GameData.HumanPlayerID, (TechnologyAdvanceEnum)suggestedResearchField) || this.parent.CAPI.RNG.Next(2) == 0)
						{
							if (researchMaximumScore != -1)
							{
								// Instruction address 0x1ade:0x168a, size: 3
								F0_1ade_1d2e_ResearchFinished(playerID, playerID1, (TechnologyAdvanceEnum)suggestedResearchField);

								// Instruction address 0x1ade:0x1697, size: 5
								this.parent.AIEngine.F0_25fb_3459_PlayerChangeCityProductionForSameContinent(playerID, -1);
							}
						}

						break;
					}

					if (this.parent.GameData.Players[this.parent.GameData.HumanPlayerID].ResearchTechnologyID != -1)
					{
						int researchFileID = this.parent.Encyclopedia.GetTechnologyAdvanceFileID(this.parent.GameData.Players[this.parent.GameData.HumanPlayerID].ResearchTechnologyID);
						bool researchAnimation = false;

						if (this.parent.GameData.Players[this.parent.GameData.HumanPlayerID].ResearchTechnologyID != 71 && this.parent.Var_deba > 3 &&
							this.parent.GameData.GameSettingFlags.Animations && !this.parent.GlobalPreferences.Debug.FastMode)
						{
							researchAnimation = true;
						}

						if (researchAnimation)
						{
							researchFieldCount = 0;

							// Instruction address 0x1ade:0x1738, size: 5
							this.parent.ImageTools.F0_2fa1_01a2_LoadBitmapOrPalette(1, 0, 0,
								$"DISCOVR{(F0_1ade_22b5_PlayerHasTechnology(this.parent.GameData.HumanPlayerID, TechnologyAdvanceEnum.Electricity) ? "2" : "1")}.PIC", 0);

							// Instruction address 0x1ade:0x1753, size: 5
							this.parent.DrawTools.FillRectangle(this.parent.Var_aa_Screen0_Rectangle, 0, 0, 320, 200, 0);

							// Instruction address 0x1ade:0x175b, size: 5
							this.parent.CommonTools.StopPaletteCycleSlots();

							// Instruction address 0x1ade:0x1764, size: 5
							this.parent.ImageTools.F0_2fa1_01a2_LoadBitmapOrPalette(-1, 0, 0,
								$"DISCOVR{(F0_1ade_22b5_PlayerHasTechnology(this.parent.GameData.HumanPlayerID, TechnologyAdvanceEnum.Electricity) ? "2" : "1")}.PIC", 1);

							// Instruction address 0x1ade:0x1784, size: 5
							this.parent.Graphics.F0_VGA_07d8_DrawImage(this.parent.Var_19d4_Screen1_Rectangle, 0, 0, 320, 200, this.parent.Var_aa_Screen0_Rectangle, 0, 0);
						}

						this.parent.CommonTools.PlayTune(this.parent.GameData.Nations[this.parent.GameData.Players[playerID].NationalityID].ShortTune, 0);

						string discoveryText = $"{this.parent.GameData.Players[playerID].Nationality} " +
							$"{(F0_1ade_22b5_PlayerHasTechnology(this.parent.GameData.HumanPlayerID, TechnologyAdvanceEnum.Invention) ? "scientists" : "wise men")}" +
							$"\ndiscover the secret\nof " +
							$"{this.parent.GameData.TechnologyAdvances[this.parent.GameData.Players[this.parent.GameData.HumanPlayerID].ResearchTechnologyID].Name}" +
							$"{((this.parent.GameData.Players[this.parent.GameData.HumanPlayerID].ResearchTechnologyID == 71) ?
								$" {++this.parent.GameData.Players[this.parent.GameData.HumanPlayerID].FutureTechnologyCount}" : "")}!\n";

						if (!researchAnimation)
						{
							this.parent.News.F21_0000_0000_ShowNews(-1, discoveryText);
						}
						else
						{
							if (researchFieldCount == 0)
							{
								this.parent.Var_aa_Screen0_Rectangle.FontID = 6;
							}

							this.parent.Var_db38 = 1;

							// Instruction address 0x1ade:0x188b, size: 5
							this.parent.MenuBoxDialog.F0_2d05_0031_ShowMenuBox(discoveryText, 96, (researchFieldCount != 0) ? 25 : 1, false, false, true);

							this.parent.Var_aa_Screen0_Rectangle.FontID = 1;

							// Instruction address 0x1ade:0x18d3, size: 5
							this.parent.ImageTools.F0_2fa1_01a2_LoadBitmapOrPalette(1, 0, 0, $"ICONPG{(researchFileID / 9 + 1)}.PIC", 0);

							this.parent.Encyclopedia.DrawEncyclopediaImage(
								((researchFileID % 3) * 111) + 1, (((researchFileID % 9) / 3) * 69) + 1, 110, 68, 174, (researchFieldCount != 0 ? 87 : 96));

							byte[] palette; // 0xc1d6
							byte[] palette1; // 0xc5be

							// Instruction address 0x1ade:0x1958, size: 5
							this.parent.ImageTools.F0_2fa1_01a2_LoadBitmapOrPalette(-1, 0, 0,
								$"DISCOVR{(F0_1ade_22b5_PlayerHasTechnology(this.parent.GameData.HumanPlayerID, TechnologyAdvanceEnum.Electricity) ? "2" : "1")}.PIC", out palette);

							// Instruction address 0x1ade:0x1968, size: 5
							this.parent.ImageTools.F0_2fa1_01a2_LoadBitmapOrPalette(-1, 0, 0, "ICONPG1.PAL", out palette1);

							// this.oParent.CAPI.memcpy(0xc5c4, 0xc1dc, 255); // 0xc5c4 - 0xc5be = 6; 0xc1dc - 0xc1d6 = 6; 255 / 3 = 85

							for (int i = 0; i < 255; i++)
							{
								palette1[i + 6] = palette[i + 6];
							}

							// Instruction address 0x1ade:0x198c, size: 5
							this.parent.CommonTools.TransformPalette(30, palette1);

							// Instruction address 0x1ade:0x1994, size: 5
							this.parent.Segment_2459.F0_2459_0918_WaitForKeyPressOrMouseClick();

							// Instruction address 0x1ade:0x199e, size: 5
							this.parent.CommonTools.StartPaletteCycleSlots();
						}

						// Instruction address 0x1ade:0x19bc, size: 3
						F0_1ade_1d2e_ResearchFinished(playerID, playerID1, (TechnologyAdvanceEnum)this.parent.GameData.Players[this.parent.GameData.HumanPlayerID].ResearchTechnologyID);

						// Instruction address 0x1ade:0x19c6, size: 5
						this.parent.CommonTools.PlayTune(1, 0);

						this.parent.GameData.Players[this.parent.GameData.HumanPlayerID].ResearchTechnologyID = -1;
					}

					if (this.parent.GameData.Players[playerID].DiscoveredTechnologyCount <= 1)
					{
						this.parent.Help.F4_0000_02d3_ShowInstantAdvicePopup("*FIRSTCIV");
					}

					this.parent.Graphics.F0_VGA_07d8_DrawImage(this.parent.Var_aa_Screen0_Rectangle, 0, 0, 320, 200, this.parent.Var_19d4_Screen1_Rectangle, 0, 0);

					StringBuilder researchList = new StringBuilder();

					if (this.parent.GameData.GameSettingFlags.InstantAdvice && suggestedResearchField != -1 && this.parent.GameData.DifficultyLevel == 0)
					{
						researchFieldCount = 0;

						researchList.Append("So we can build:\n");

						for (int i = 0; i < 28; i++)
						{
							if (this.parent.GameData.Units[i].RequiredTechnology == (TechnologyAdvanceEnum)suggestedResearchField)
							{
								// Instruction address 0x1ade:0x1a92, size: 5
								researchList.Append($"-{this.parent.GameData.Units[i].Name}\n");

								researchFieldCount++;
							}
						}

						for (int i = 0; i < 24; i++)
						{
							if (this.parent.GameData.GetImprovementType(i).RequiresTechnology == (TechnologyAdvanceEnum)suggestedResearchField)
							{
								// Instruction address 0x1ade:0x1ad6, size: 5
								researchList.Append($"-{this.parent.GameData.GetImprovementType(i).Name}\n");

								researchFieldCount++;
							}
						}

						if (researchFieldCount == 0)
						{
							researchList.Clear();
						}

						this.parent.Var_aa_Screen0_Rectangle.FontID = 2;

						this.parent.Var_db38 = 1;

						// Instruction address 0x1ade:0x1b2c, size: 5
						this.parent.MenuBoxDialog.F0_2d05_0031_ShowMenuBox(
							$"Science Advisor:\nI recommend we\nresearch {this.parent.GameData.TechnologyAdvances[suggestedResearchField].Name}.\n{researchList.ToString()}",
							8, 168 - (researchFieldCount * 8), true, false, true);

						this.parent.Var_aa_Screen0_Rectangle.FontID = 1;
					}

					researchList = new StringBuilder();

					researchList.Append("Which research field should our\n" +
						$"{(F0_1ade_22b5_PlayerHasTechnology(this.parent.GameData.HumanPlayerID, TechnologyAdvanceEnum.Invention) ? "scientists" : "wise men")} " +
						"be pursuing, sire?\nPick one...\n ");

					researchFieldCount = 0;

					for (int i = 0; i < 72; i++)
					{
						// Instruction address 0x1ade:0x1b96, size: 3
						if (!F0_1ade_22b5_PlayerHasTechnology(playerID, (TechnologyAdvanceEnum)i) &&
							F0_1ade_22b5_PlayerHasTechnology(playerID, this.parent.GameData.TechnologyAdvances[i].RequiresTechnologyAdvance1))
						{
							// Instruction address 0x1ade:0x1bcc, size: 3
							if (F0_1ade_22b5_PlayerHasTechnology(playerID, this.parent.GameData.TechnologyAdvances[i].RequiresTechnologyAdvance2))
							{
								if ((firstPass && (researchFieldCount <= 1 || (i - this.parent.GameData.Players[playerID].DiscoveredTechnologyCount) % 3 != 0 ||
										this.parent.GameData.DifficultyLevel == 0)) ||
									(!firstPass && researchIDList[researchFieldCount] == i))
								{
									// Instruction address 0x1ade:0x1c20, size: 5
									researchList.Append($"{this.parent.GameData.TechnologyAdvances[i].Name}\n ");

									researchIDList[researchFieldCount] = i;

									researchFieldCount++;
								}
							}
						}
					}

					this.parent.Var_2f9e_MessageBoxStyle = MenuBoxReportTypeEnum.ScienceAdvisorReport;

					if (researchFieldCount == 0)
					{
						// Instruction address 0x1ade:0x1d18, size: 5
						this.parent.MenuBoxDialog.ShowDialog($" Future Technology {(this.parent.GameData.Players[this.parent.GameData.HumanPlayerID].FutureTechnologyCount + 1)}\n", 100, 80);

						this.parent.GameData.Players[this.parent.GameData.HumanPlayerID].ResearchTechnologyID = 71;

						break;
					}

					// Instruction address 0x1ade:0x1c70, size: 5
					int newResearchSelection = this.parent.MenuBoxDialog.F0_2d05_0031_ShowMenuBox(researchList.ToString(), 80, 64, true, true, true);

					this.parent.Graphics.F0_VGA_07d8_DrawImage(this.parent.Var_19d4_Screen1_Rectangle, 0, 0, 320, 200, this.parent.Var_aa_Screen0_Rectangle, 0, 0);

					if (newResearchSelection == -1)
					{
						newResearchSelection = 0;
					}

					newResearchSelection = researchIDList[newResearchSelection];

					if (!this.parent.Var_2f9c_MenuBoxHelpRequested)
					{
						this.parent.GameData.Players[this.parent.GameData.HumanPlayerID].ResearchTechnologyID = (short)newResearchSelection;
						break;
					}

					this.parent.Encyclopedia.DisplayEncyclopediaTopic(EncyclopediaTopicEnum.TechnologyAdvance, newResearchSelection);

					// Instruction address 0x1ade:0x1cb5, size: 5
					this.parent.GameEngine.F0_1238_1b44_RedrawMapWithBlankingEffect();

					firstPass = false;
				}
			}
		}

		/// <summary>
		/// Player 1 finishes researching technology, or acquires technology from player 2, or Great Library
		/// </summary>
		/// <param name="playerID1"></param>
		/// <param name="playerID2"></param>
		/// <param name="technology"></param>
		public void F0_1ade_1d2e_ResearchFinished(int playerID1, int playerID2, TechnologyAdvanceEnum technology)
		{
			//this.CPU.Log.EnterBlock($"F0_1ade_1d2e({playerID1}, {technology}, {playerID2})");

			// function body
			// Instruction address 0x1ade:0x1d3d, size: 3
			if (!F0_1ade_22b5_PlayerHasTechnology(playerID1, technology))
			{
				if (technology != TechnologyAdvanceEnum.FutureTechnology5)
				{
					this.parent.GameData.Players[playerID1].DiscoveredTechnologyFlags[(int)technology / 16] |= (ushort)(0x1 << ((int)technology % 16));
					this.parent.GameData.Players[playerID1].TechnologyAcquiredFrom[(int)technology] = (short)playerID2;
				}

				if (this.parent.GameData.TurnCount != 0)
				{
					this.parent.GameData.Players[playerID1].DiscoveredTechnologyCount++;

					if (playerID1 == this.parent.GameData.HumanPlayerID ||
						this.parent.GameData.Players[this.parent.GameData.HumanPlayerID].Diplomacy[playerID1].HasFlag(DiplomacyFlagsEnum.EmbassyEstablished))
					{
						// Instruction address 0x1ade:0x1de0, size: 5
						StringBuilder researchText = new StringBuilder();

						researchText.Append($"{this.parent.GameData.Players[playerID1].Nation}{((playerID1 == playerID2 || playerID2 == 0) ? " develop" : " acquire")}\n");
						researchText.Append($"{this.parent.GameData.TechnologyAdvances[(int)technology].Name}");
						researchText.Append($"{((playerID1 != playerID2 && playerID2 != 0) ? ($" from\nthe {((playerID2 == -2) ? "Great Library" : this.parent.GameData.Players[playerID2].Nation)}") : "")}!\n ");

						if (playerID1 != this.parent.GameData.HumanPlayerID)
						{
							for (int i = 0; i < 24; i++)
							{
								if (this.parent.GameData.GetImprovementType(i).RequiresTechnology == technology)
								{
									// Instruction address 0x1ade:0x1e97, size: 5
									researchText.Append($"{this.parent.GameData.GetImprovementType(i).Name}\n ");
								}
							}

							for (int i = 0; i < 28; i++)
							{
								if (this.parent.GameData.Units[i].RequiredTechnology == (TechnologyAdvanceEnum)technology)
								{
									// Instruction address 0x1ade:0x1ed8, size: 5
									researchText.Append($"{this.parent.GameData.Units[i].Name}\n ");
								}
							}
						}

						if ((playerID1 != this.parent.GameData.HumanPlayerID || playerID2 == -2) && this.parent.Var_3934 == -1)
						{
							if (playerID1 != this.parent.GameData.HumanPlayerID)
							{
								this.parent.Var_2f9e_MessageBoxStyle = MenuBoxReportTypeEnum.SpiesReport;
							}
							else
							{
								this.parent.Var_2f9e_MessageBoxStyle = MenuBoxReportTypeEnum.ScienceAdvisorReport;
							}

							// Instruction address 0x1ade:0x1f3c, size: 5
							this.parent.MenuBoxDialog.ShowDialog(researchText.ToString(), 100, 80);
						}
					}
					else if ((this.parent.GameData.TechnologyFirstDiscoveredBy[(int)technology] & 0x7) == 0 &&
						F0_1ade_22b5_PlayerHasTechnology(playerID1, TechnologyAdvanceEnum.Navigation) &&
						!F0_1ade_22b5_PlayerHasTechnology(this.parent.GameData.HumanPlayerID, technology) &&
						this.parent.CAPI.RNG.Next(3) == 0 && (short)this.parent.MeetWithKing.F6_0000_16cd(playerID1, -1, 0) != -1)
					{
						this.parent.Var_3934 = 0;
						this.parent.Var_d804 = 0;

						// Instruction address 0x1ade:0x1fc4, size: 5
						this.parent.Array_30b8[0] = this.parent.GameData.Players[playerID1].Nation;

						// Instruction address 0x1ade:0x1fda, size: 5
						this.parent.Array_30b8[3] = this.parent.GameData.TechnologyAdvances[(int)technology].Name;

						this.parent.Var_3936 = 2;
						this.parent.MeetWithKing.Var_d76c = 12;

						this.parent.MeetWithKing.F6_0000_251d_ShowInlineDialog(this.parent.LanguageTools.F0_2f4d_044f_GetTextFromKingSection("*GAPE"), 36, 139);

						this.parent.MeetWithKing.F6_0000_16ac();
					}

					if (playerID1 == this.parent.GameData.HumanPlayerID)
					{
						if (technology != TechnologyAdvanceEnum.FutureTechnology5)
						{
							this.parent.Encyclopedia.DisplayEncyclopediaTopic(EncyclopediaTopicEnum.TechnologyAdvance, (int)technology);
						}

						if (technology == TechnologyAdvanceEnum.Invention)
						{
							this.parent.StartGameMenu.F5_0000_1af6_LoadGovernmentImage();

							// Instruction address 0x1ade:0x203a, size: 5
							this.parent.GameEngine.F0_1238_1b44_RedrawMapWithBlankingEffect();
						}

						if (this.parent.Var_3934 == -1)
						{
							// Instruction address 0x1ade:0x2049, size: 5
							this.parent.GameEngine.F0_1238_1b44_RedrawMapWithBlankingEffect();

							if (technology == TechnologyAdvanceEnum.Monarchy)
							{
								this.parent.Help.F4_0000_02d3_ShowInstantAdvicePopup("*MONARCHY");
							}
						}
					}
					else
					{
						// !!! Exchange of Ambassadors, rule is never invoked!
						// This piece of code is always skipped, as the result is always 0 or 1, and bit 0x40 is always 0!

						if (this.parent.GameData.DifficultyLevel == 0 && technology == TechnologyAdvanceEnum.Writing &&
							((this.parent.GameData.Players[this.parent.GameData.HumanPlayerID].Diplomacy[playerID1] == DiplomacyFlagsEnum.None ? 1 : 0) & 0x40) != 0 &&
							this.parent.Var_3934 == -1)
						{
							this.parent.GameData.Players[this.parent.GameData.HumanPlayerID].Diplomacy[playerID1] |= DiplomacyFlagsEnum.EmbassyEstablished;

							this.parent.Var_2f9e_MessageBoxStyle = MenuBoxReportTypeEnum.DiplomatsReport;

							// Instruction address 0x1ade:0x20cc, size: 5
							this.parent.MenuBoxDialog.ShowDialog($"{this.parent.GameData.Players[playerID1].Nation} agree to\nan exchange of ambassadors.\n", 80, 100);

							this.parent.Overlay_14.F14_0000_0d43_IntelligenceReport();
						}
					}

					if (technology == TechnologyAdvanceEnum.Gunpowder || technology == TechnologyAdvanceEnum.Combustion)
					{
						for (int i = 0; i < 128; i++)
						{
							if (this.parent.GameData.Cities[i].Status != CityStatusEnum.Invalid &&
								this.parent.GameData.Cities[i].PlayerID == playerID1)
							{
								this.parent.GameData.Cities[i].ImprovementFlags0 &= 0xfffd;
							}
						}

						if (playerID1 == this.parent.GameData.HumanPlayerID && this.parent.Var_3934 == -1)
						{
							this.parent.Var_2f9e_MessageBoxStyle = MenuBoxReportTypeEnum.DefenseMinisterReport;

							// Instruction address 0x1ade:0x216c, size: 5
							this.parent.MenuBoxDialog.ShowDialog(
								$"Development of {this.parent.GameData.TechnologyAdvances[(int)technology].Name}\nmakes existing {this.parent.GameData.GetImprovementType((int)ImprovementEnum.Barracks).Name}\nobsolete.\n", 80, 100);
						}
					}

					if ((this.parent.GameData.TechnologyFirstDiscoveredBy[(int)technology] & 0x7) == 0)
					{
						this.parent.GameData.TechnologyFirstDiscoveredBy[(int)technology] = (short)playerID1;

						// Instruction address 0x1ade:0x21a1, size: 5
						this.parent.UnitManagement.F0_1866_250e_AddReplayData(5, (byte)playerID1, (byte)technology);

						for (int i = 1; i < 22; i++)
						{
							if (this.parent.GameData.WonderCityID[i] != -1 &&
								this.parent.GameData.Wonders[i].ObsoletesAfterTechnology == (TechnologyAdvanceEnum)technology)
							{
								if (this.parent.GameData.Cities[this.parent.GameData.WonderCityID[i]].PlayerID == this.parent.GameData.HumanPlayerID)
								{
									this.parent.News.F21_0000_0000_ShowNews(-1,
										$"{this.parent.GameData.TechnologyAdvances[(int)technology].Name} cancels\n" +
										$"the effect of\n{this.parent.GameData.Wonders[i].Name}.\n");
								}
							}
						}
					}

					int greatLibraryCityID = this.parent.GameData.WonderCityID[(int)WonderEnum.GreatLibrary];

					if (greatLibraryCityID != -1)
					{
						int greatLibraryPlayerID = this.parent.GameData.Cities[greatLibraryCityID].PlayerID;

						// Instruction address 0x1ade:0x2254, size: 3
						if (!F0_1ade_22b5_PlayerHasTechnology(greatLibraryPlayerID, technology))
						{
							// Instruction address 0x1ade:0x2265, size: 5
							if (!this.parent.CityWorker.F0_1d12_6c97_PlayerHasWonder(greatLibraryPlayerID, WonderEnum.GreatLibrary))
							{
								int playerCount = 0;

								for (int i = 1; i < 8; i++)
								{
									// Instruction address 0x1ade:0x2282, size: 3
									if (F0_1ade_22b5_PlayerHasTechnology(i, technology))
									{
										playerCount++;
									}
								}

								if (playerCount >= 2)
								{
									// Instruction address 0x1ade:0x22a9, size: 3
									F0_1ade_1d2e_ResearchFinished(greatLibraryPlayerID, -2, technology);
								}
							}
						}
					}
				}
			}
		}

		/// <summary>
		/// ?
		/// </summary>
		/// <param name="playerID"></param>
		/// <param name="technology"></param>
		/// <returns></returns>
		public bool F0_1ade_22b5_PlayerHasTechnology(int playerID, TechnologyAdvanceEnum technology)
		{
			// Everyone has TechnologyEnum.None (-1) technology,
			// PlayerID == 0 (Barbarians can't have any technology)
			// TechnologyID can be in the range [0-70]
			// DiscoveredTechnologyFlags holds the bit if technology is discovered

			return (technology == TechnologyAdvanceEnum.None ||
				(playerID != 0 && technology != TechnologyAdvanceEnum.None && technology != TechnologyAdvanceEnum.NewFutureTechnology &&
					(this.parent.GameData.Players[playerID].DiscoveredTechnologyFlags[(int)technology >> 4] & (1 << ((int)technology & 0xf))) != 0)) ? true : false;

			//return (ushort)((technologyID == -1 || (technologyID >= 0 && (technologyID >= 71 || playerID == 0 ||
			//	(this.oParent.CivState.Players[playerID].DiscoveredTechnologyFlags[technologyID >> 4] & (1 << (technologyID & 0xf))) == 0))) ? 0 : 1);
		}

		private static int[] technologyGradeCoefficient1 = { 5, 4, 4, 4, 3, 6, 4, 6, 6, 6, 4, 3, 4, 3, 3, 3, 3, 6, 5, 3, 4, 3, 3, 4, 4, 5, 4, 8, 3, 3, 3, 4, 5, 4, 8, 5, 5, 6, 4, 5, 4, 6, 6, 2, 5, 4, 6, 4, 6, 4, 3, 3, 4, 4, 3, 4, 5, 2, 8, 2, 4, 2, 5, 5, 6, 4, 3, 0, 0, 0, 0, 0 };
		private static int[] technologyGradeCoefficient2 = { 0, 1, 1, 0, 2, 1, 0, -1, -1, -1, 0, 0, 0, 1, -2, 0, 1, -1, -2, 0, 0, 0, 1, -1, 1, 0, 0, -2, 1, 2, 2, -1, -1, -2, -2, -1, -1, -1, -1, -1, 1, 0, 1, 0, 1, 0, -2, 0, -2, 0, 0, 1, -1, 1, 1, 0, -1, 1, -1, 2, 1, 1, -2, -1, -1, -1, 0, 0, 0, 0, 0, 0 };

		/// <summary>
		/// Grade technology in according with player ideology
		/// </summary>
		/// <param name="playerID"></param>
		/// <param name="technologyID"></param>
		public int F0_1ade_2317_GradeTechnology(int playerID, TechnologyAdvanceEnum technology)
		{
			//this.oCPU.Log.EnterBlock($"F0_1ade_2317({playerID}, {TechnologyID})");

			// function body
			//this.oCPU.CX.Int16 = this.oCPU.ReadInt8(this.oCPU.DS.UInt16, (ushort)(0xb0a + (technologyID * 2)));
			//this.oCPU.BX.Int16 = this.oCPU.ReadInt8(this.oCPU.DS.UInt16, (ushort)(0xb0b + (technologyID * 2)));

			return (this.parent.GameData.Nations[this.parent.GameData.Players[playerID].NationalityID].Ideology * technologyGradeCoefficient2[(int)technology]) +
				technologyGradeCoefficient1[(int)technology];
		}
	}
}
