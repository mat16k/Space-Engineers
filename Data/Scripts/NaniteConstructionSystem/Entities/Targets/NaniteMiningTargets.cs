﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.ModAPI;
using VRageMath;
using VRage.ModAPI;
using VRage.Game.Entity;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game;
using Sandbox.Definitions;
using Ingame = Sandbox.ModAPI.Ingame;
using VRage.Game;
using VRage.Voxels;
using VRage.ObjectBuilders;

using NaniteConstructionSystem.Particles;
using NaniteConstructionSystem.Extensions;
using NaniteConstructionSystem.Entities.Beacons;

namespace NaniteConstructionSystem.Entities.Targets
{
    public class NaniteMiningTarget
    {
        public int ParticleCount { get; set; }
        public double StartTime { get; set; }
        public double CarryTime { get; set; }
        public double LastUpdate { get; set; }
    }

    public class NaniteMiningTargets : NaniteTargetBlocksBase
    {
        public override string TargetName
        {
            get
            {
                return "Mining";
            }
        }

        private float m_maxDistance = 500f;
        private Dictionary<NaniteMiningItem, NaniteMiningTarget> m_targetTracker;
        private static HashSet<Vector3D> m_globalPositionList;
        private Random rnd;

        public NaniteMiningTargets(NaniteConstructionBlock constructionBlock) : base(constructionBlock)
        {
            m_maxDistance = NaniteConstructionManager.Settings.MiningMaxDistance;
            m_targetTracker = new Dictionary<NaniteMiningItem, NaniteMiningTarget>();
            m_globalPositionList = new HashSet<Vector3D>();
            rnd = new Random();
        }

        public override int GetMaximumTargets()
        {
            MyCubeBlock block = (MyCubeBlock)m_constructionBlock.ConstructionBlock;
            return (int)Math.Min(NaniteConstructionManager.Settings.MiningNanitesNoUpgrade + (block.UpgradeValues["MiningNanites"] * NaniteConstructionManager.Settings.MiningNanitesPerUpgrade), NaniteConstructionManager.Settings.MiningMaxStreams);
        }

        public override float GetPowerUsage()
        {
            MyCubeBlock block = (MyCubeBlock)m_constructionBlock.ConstructionBlock;
            return Math.Max(1, NaniteConstructionManager.Settings.MiningPowerPerStream - (int)(block.UpgradeValues["PowerNanites"] * NaniteConstructionManager.Settings.PowerDecreasePerUpgrade));
        }

        public override float GetMinTravelTime()
        {
            MyCubeBlock block = (MyCubeBlock)m_constructionBlock.ConstructionBlock;
            return Math.Max(1f, NaniteConstructionManager.Settings.MiningMinTravelTime - (block.UpgradeValues["SpeedNanites"] * NaniteConstructionManager.Settings.MinTravelTimeReductionPerUpgrade));
        }

        public override float GetSpeed()
        {
            MyCubeBlock block = (MyCubeBlock)m_constructionBlock.ConstructionBlock;
            return NaniteConstructionManager.Settings.MiningDistanceDivisor + (block.UpgradeValues["SpeedNanites"] * (float)NaniteConstructionManager.Settings.SpeedIncreasePerUpgrade);
        }

        public override bool IsEnabled()
        {
            bool result = true;
            if (!((IMyFunctionalBlock)m_constructionBlock.ConstructionBlock).Enabled ||
                !((IMyFunctionalBlock)m_constructionBlock.ConstructionBlock).IsFunctional ||
                m_constructionBlock.ConstructionBlock.CustomName.ToLower().Contains("NoMining".ToLower()))
                result = false;

            if (NaniteConstructionManager.TerminalSettings.ContainsKey(m_constructionBlock.ConstructionBlock.EntityId))
            {
                if (!NaniteConstructionManager.TerminalSettings[m_constructionBlock.ConstructionBlock.EntityId].AllowMining)
                    return false;
            }

            return result;
        }

        public override void ParallelUpdate(List<IMyCubeGrid> gridList, List<IMySlimBlock> gridBlocks)
        {
            using (Lock.AcquireExclusiveUsing())
                PotentialTargetList.Clear();

            DateTime start = DateTime.Now;
            List<object> finalAddList = new List<object>();
            int listCount = 0;
            foreach (var miningBlock in NaniteConstructionManager.MiningList.Where(x => x.IsWorking && Vector3D.DistanceSquared(m_constructionBlock.ConstructionBlock.GetPosition(), x.MiningBlock.GetPosition()) < m_maxDistance * m_maxDistance).OrderBy(x => rnd.Next(100)))
            {
                IMyCubeBlock item = (IMyCubeBlock)miningBlock.MiningBlock;
                MyRelationsBetweenPlayerAndBlock relation = item.GetUserRelationToOwner(m_constructionBlock.ConstructionBlock.OwnerId);
                if (!(relation == MyRelationsBetweenPlayerAndBlock.Owner || relation == MyRelationsBetweenPlayerAndBlock.FactionShare || (MyAPIGateway.Session.CreativeMode && relation == MyRelationsBetweenPlayerAndBlock.NoOwnership)))
                    continue;

                if (!((IMyFunctionalBlock)item).Enabled)
                    continue;

                if (miningBlock.OreList == null || miningBlock.OreList.Count < 1)
                    continue;

                int sum = miningBlock.OreList.Sum(x => x.Value.Count);
                Dictionary<MyVoxelMaterialDefinition, List<NaniteMiningItem>> lookup = null;
                using (miningBlock.Lock.AcquireExclusiveUsing())
                {
                    lookup = miningBlock.OreList.ToDictionary(x => x.Key, x => x.Value);
                }

                List<object> addList = new List<object>();
                int count = 0;
                int pos = 0;

                while (true)
                {                    
                    var group = lookup.ElementAt(count % miningBlock.OreList.Count);
                    if (pos < group.Value.Count)
                    {
                        addList.Insert(0, group.Value[pos]);
                    }
                    
                    count++;
                    if (count % miningBlock.OreList.Count == 0)
                        pos++;

                    if (count >= 1000)
                        break;

                    if (count >= sum)
                        break;
                }

                DistributeList(addList, finalAddList, listCount);
                listCount++;

                if (listCount > 5)
                    break;
            }

            var listToAdd = finalAddList.Take(1000).ToList();
            listToAdd.Reverse();
            using (Lock.AcquireExclusiveUsing())
            {
                PotentialTargetList.AddRange(listToAdd);
            }

            //Logging.Instance.WriteLine(string.Format("ParallelUpdate() took {0} ms", (DateTime.Now - start).TotalMilliseconds));
        }

        private void DistributeList(List<object> listToAdd, List<object> finalList, int count)
        {
            if(count < 1)
            {
                finalList.AddRange(listToAdd);
                return;
            }

            for(int r = listToAdd.Count - 1; r >= 0; r--)
            {
                var item = listToAdd[r];
                var realPos = r * (count + 1);
                if (realPos >= finalList.Count)
                    realPos = finalList.Count - 1;

                finalList.Insert(realPos, item);
            }
        }

        public override void FindTargets(ref Dictionary<string, int> available)
        {
            if (!IsEnabled())
                return;

            if (TargetList.Count >= GetMaximumTargets())
            {
                if (PotentialTargetList.Count > 0)
                    m_lastInvalidTargetReason = "Maximum targets reached.  Add more upgrades!";

                return;
            }

            DateTime start = DateTime.Now;
            using (Lock.AcquireExclusiveUsing())
            {
                if (m_constructionBlock.IsUserDefinedLimitReached())
                {
                    m_lastInvalidTargetReason = "User defined maximum nanite limit reached";
                    return;
                }

                //foreach (NaniteMiningItem item in PotentialTargetList)
                for (int r = PotentialTargetList.Count - 1; r >= 0; r--)
                {
                    var item = (NaniteMiningItem)PotentialTargetList[r];
                    if (TargetList.Contains(item))
                        continue;

                    if (m_globalPositionList.Contains(item.Position))
                    {
                        m_lastInvalidTargetReason = "Another factory has this voxel as a target";
                        continue;
                    }

                    var blockList = NaniteConstructionManager.GetConstructionBlocks((IMyCubeGrid)m_constructionBlock.ConstructionBlock.CubeGrid);
                    bool found = false;
                    foreach (var block in blockList)
                    {
                        
                        // This can be sped up if necessary by indexing items by position
                        if (block.GetTarget<NaniteMiningTargets>().TargetList.FirstOrDefault(x => ((NaniteMiningItem)x).Position == item.Position) != null)
                        {
                            found = true;
                            break;
                        }
                    }

                    if (found)
                    {
                        m_lastInvalidTargetReason = "Another factory has this voxel as a target";
                        continue;
                    }

                    if (!NaniteMining.CheckVoxelContent(item.VoxelId, item.Position))
                    {
                        continue;
                    }

                    if (Vector3D.DistanceSquared(m_constructionBlock.ConstructionBlock.GetPosition(), item.Position) < m_maxDistance * m_maxDistance &&
                       NaniteConstructionPower.HasRequiredPowerForNewTarget((IMyFunctionalBlock)m_constructionBlock.ConstructionBlock, this))
                    {
                        Logging.Instance.WriteLine(string.Format("ADDING Mining Target: conid={0} pos={1} type={2}", m_constructionBlock.ConstructionBlock.EntityId, item.Position, MyDefinitionManager.Static.GetVoxelMaterialDefinition(item.VoxelMaterial).MinedOre));

                        //PotentialTargetList.Remove(item);
                        TargetList.Add(item);
                        m_globalPositionList.Add(item.Position);
                        if (TargetList.Count >= GetMaximumTargets())
                            break;
                    }
                }
            }

            //Logging.Instance.WriteLine(string.Format("FindTargets took {0}ms", (DateTime.Now - start).TotalMilliseconds));
        }

        public override void Update()
        {
            foreach(var item in TargetList.ToList())
            {
                ProcessItem(item);
            }            
        }

        private void ProcessItem(object miningTarget)
        {
            var target = miningTarget as NaniteMiningItem;
            if (target == null)
                return;

            if (Sync.IsServer)
            {
                if (!IsEnabled())
                {
                    Logging.Instance.WriteLine("CANCELLING Mining Target due to being disabled");
                    CancelTarget(target);
                    return;
                }

                if (m_constructionBlock.FactoryState != NaniteConstructionBlock.FactoryStates.Active)
                    return;

                if(!target.MiningHammer.IsWorking)
                {
                    Logging.Instance.WriteLine("CANCELLING Mining Target due to hammer functionality change");
                    CancelTarget(target);
                    return;
                }

                if (!NaniteConstructionPower.HasRequiredPowerForCurrentTarget((IMyFunctionalBlock)m_constructionBlock.ConstructionBlock))
                {
                    Logging.Instance.WriteLine("CANCELLING Mining Target due to power shortage");
                    CancelTarget(target);
                    return;
                }

                if (!m_targetTracker.ContainsKey(target))
                {
                    m_constructionBlock.SendAddTarget(target);
                }

                if (m_targetTracker.ContainsKey(target))
                {
                    var trackedItem = m_targetTracker[target];
                    if (MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds - trackedItem.StartTime >= trackedItem.CarryTime &&
                        MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds - trackedItem.LastUpdate > 2000)
                    {
                        trackedItem.LastUpdate = MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds;

                        if (!TransferFromTarget(target))
                            CancelTarget(target);
                        else
                            CompleteTarget(target);
                    }
                }
            }

            CreateMiningParticles(target);
        }

        private void CreateMiningParticles(NaniteMiningItem target)
        {
            if (!m_targetTracker.ContainsKey(target))
                CreateTrackerItem(target);

            if (NaniteParticleManager.TotalParticleCount > NaniteParticleManager.MaxTotalParticles)
                return;

            m_targetTracker[target].ParticleCount++;
            int size = (int)Math.Max(60f, NaniteParticleManager.TotalParticleCount);
            if ((float)m_targetTracker[target].ParticleCount / size < 1f)
                return;

            m_targetTracker[target].ParticleCount = 0;

            // Create Particle
            Vector4 startColor = new Vector4(0.7f, 0.2f, 0.0f, 1f);
            Vector4 endColor = new Vector4(0.2f, 0.05f, 0.0f, 0.35f);
            m_constructionBlock.ParticleManager.AddParticle(startColor, endColor, GetMinTravelTime() * 1000f, GetSpeed(), target, (IMyTerminalBlock)target.MiningHammer.MiningBlock);
        }

        private void CreateTrackerItem(NaniteMiningItem target)
        {
            double distance = Vector3D.Distance(m_constructionBlock.ConstructionBlock.GetPosition(), target.Position);
            int time = (int)Math.Max(GetMinTravelTime() * 1000f, (distance / GetSpeed()) * 1000f);

            NaniteMiningTarget miningTarget = new NaniteMiningTarget();
            miningTarget.ParticleCount = 0;
            miningTarget.StartTime = MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds;
            miningTarget.LastUpdate = MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds;
            miningTarget.CarryTime = time - 1000;
            m_targetTracker.Add(target, miningTarget);
        }

        private bool TransferFromTarget(NaniteMiningItem target)
        {
            byte material = 0;
            float amount = 0;
            //NaniteMining.CheckVoxelContent(target.VoxelId, target.Position, target.LocalPos);
            NaniteMining.RemoveVoxelContent(target.VoxelId, target.Position, out material, out amount);
            //NaniteMining.TestRemoveVoxel(target.VoxelId, target.Position, out material, out amount);
            //gging.Instance.WriteLine($"Removing: {target.Position} ({material} {amount})");

            /*
            if (material == 0)
            {
                Logging.Instance.WriteLine(string.Format("Material is 0", target.VoxelId));
                return false;
            }
            */

            if (amount == 0f)
            {
                Logging.Instance.WriteLine(string.Format("Amount is 0", target.VoxelId));
                return false;
            }

            var def = MyDefinitionManager.Static.GetVoxelMaterialDefinition(target.VoxelMaterial);
            var builder = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_Ore>(def.MinedOre);
            if (!GridHelper.FindFreeCargo((MyCubeBlock)m_constructionBlock.ConstructionBlock, builder, (int)amount, false))
            {
                Logging.Instance.WriteLine(string.Format("Can not find free cargo space!"));
                return false;
            }

            return true;
        }

        public override void CancelTarget(object obj)
        {
            var target = obj as NaniteMiningItem;
            Logging.Instance.WriteLine(string.Format("CANCELLED Mining Target: {0} - {1} (VoxelID={2},Position={3})", m_constructionBlock.ConstructionBlock.EntityId, obj.GetType().Name, target.VoxelId, target.Position));
            if (Sync.IsServer)
            {
                m_constructionBlock.SendCompleteTarget((NaniteMiningItem)obj);
            }

            m_constructionBlock.ParticleManager.CancelTarget(target);
            if (m_targetTracker.ContainsKey(target))
                m_targetTracker.Remove(target);

            m_globalPositionList.Remove(target.Position);
            Remove(obj);
        }

        public override void CompleteTarget(object obj)
        {
            var target = obj as NaniteMiningItem;
            Logging.Instance.WriteLine(string.Format("COMPLETED Mining Target: {0} - {1} (VoxelID={2},Position={3})", m_constructionBlock.ConstructionBlock.EntityId, obj.GetType().Name, target.VoxelId, target.Position));
            if (Sync.IsServer)
            {
                m_constructionBlock.SendCompleteTarget((NaniteMiningItem)obj);
            }

            m_constructionBlock.ParticleManager.CompleteTarget(target);
            if (m_targetTracker.ContainsKey(target))
                m_targetTracker.Remove(target);

            m_globalPositionList.Remove(target.Position);
            Remove(obj);
        }
    }
}
