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

using NaniteConstructionSystem.Particles;
using NaniteConstructionSystem.Extensions;

namespace NaniteConstructionSystem.Entities.Targets
{
    public class NaniteFloatingTarget
    {
        public int ParticleCount { get; set; }
        public double StartTime { get; set; }
        public double CarryTime { get; set; }
        public double LastUpdate { get; set; }
    }

    public class NaniteFloatingTargets : NaniteTargetBlocksBase
    {
        public override string TargetName
        {
            get { return "Cleanup"; }
        }

        private HashSet<IMyEntity> m_entities = new HashSet<IMyEntity>();
        private Dictionary<IMyEntity, NaniteFloatingTarget> m_targetTracker;

        private float m_carryVolume = 10f;
        private float m_maxDistance = 500f;

        public NaniteFloatingTargets(NaniteConstructionBlock constructionBlock) : base(constructionBlock)
        {
            m_targetTracker = new Dictionary<IMyEntity, NaniteFloatingTarget>();
            m_carryVolume = NaniteConstructionManager.Settings.CleanupCarryVolume;
            m_maxDistance = NaniteConstructionManager.Settings.CleanupMaxDistance;
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

            using (Lock.AcquireExclusiveUsing())
            {
                for(int r = PotentialTargetList.Count - 1; r >= 0; r--)
                {
                    if (m_constructionBlock.IsUserDefinedLimitReached())
                    {
                        m_lastInvalidTargetReason = "User defined maximum nanite limit reached";
                        return;
                    }

                    var item = (IMyEntity)PotentialTargetList[r];
                    if (TargetList.Contains(item))
                        continue;

                    if (item.Closed)
                    {
                        PotentialTargetList.RemoveAt(r);
                        continue;
                    }

                    var blockList = NaniteConstructionManager.GetConstructionBlocks((IMyCubeGrid)m_constructionBlock.ConstructionBlock.CubeGrid);
                    bool found = false;
                    foreach (var block in blockList)
                    {
                        if (block.Targets.First(x => x is NaniteFloatingTargets).TargetList.Contains(item))
                        {
                            found = true;
                            break;
                        }
                    }

                    if (found)
                    {
                        m_lastInvalidTargetReason = "Another factory has this block as a target";
                        continue;
                    }

                    if (Vector3D.DistanceSquared(m_constructionBlock.ConstructionBlock.GetPosition(), item.GetPosition()) < m_maxDistance * m_maxDistance &&
                       NaniteConstructionPower.HasRequiredPowerForNewTarget((IMyFunctionalBlock)m_constructionBlock.ConstructionBlock, this))
                    {
                        TargetList.Add(item);
                        Logging.Instance.WriteLine(string.Format("ADDING Floating Object Target: conid={0} type={1} entityID={2} position={3}", m_constructionBlock.ConstructionBlock.EntityId, item.GetType().Name, item.EntityId, item.GetPosition()));
                        if (TargetList.Count >= GetMaximumTargets())
                            break;
                    }
                }
            }
        }

        public override int GetMaximumTargets()
        {
            MyCubeBlock block = (MyCubeBlock)m_constructionBlock.ConstructionBlock;
            return (int)Math.Min(NaniteConstructionManager.Settings.CleanupNanitesNoUpgrade + (block.UpgradeValues["CleanupNanites"] * NaniteConstructionManager.Settings.CleanupNanitesPerUpgrade), NaniteConstructionManager.Settings.CleanupMaxStreams);
        }

        public override float GetPowerUsage()
        {
            MyCubeBlock block = (MyCubeBlock)m_constructionBlock.ConstructionBlock;
            return Math.Max(1, NaniteConstructionManager.Settings.CleanupPowerPerStream - (int)(block.UpgradeValues["PowerNanites"] * NaniteConstructionManager.Settings.PowerDecreasePerUpgrade));
        }

        public override float GetMinTravelTime()
        {
            MyCubeBlock block = (MyCubeBlock)m_constructionBlock.ConstructionBlock;
            return Math.Max(1f, NaniteConstructionManager.Settings.CleanupMinTravelTime - (block.UpgradeValues["SpeedNanites"] * NaniteConstructionManager.Settings.MinTravelTimeReductionPerUpgrade));
        }

        public override float GetSpeed()
        {
            MyCubeBlock block = (MyCubeBlock)m_constructionBlock.ConstructionBlock;
            return NaniteConstructionManager.Settings.CleanupDistanceDivisor + (block.UpgradeValues["SpeedNanites"] * (float)NaniteConstructionManager.Settings.SpeedIncreasePerUpgrade);
        }

        public override bool IsEnabled()
        {
            bool result = true;
            if (!((IMyFunctionalBlock)m_constructionBlock.ConstructionBlock).Enabled ||
                !((IMyFunctionalBlock)m_constructionBlock.ConstructionBlock).IsFunctional ||
                m_constructionBlock.ConstructionBlock.CustomName.ToLower().Contains("NoCleanup".ToLower()))
                result = false;

            if (NaniteConstructionManager.TerminalSettings.ContainsKey(m_constructionBlock.ConstructionBlock.EntityId))
            {
                if (!NaniteConstructionManager.TerminalSettings[m_constructionBlock.ConstructionBlock.EntityId].AllowCleanup)
                    return false;
            }

            return result;
        }

        public override void Update()
        {
            foreach (var item in m_targetList.ToList())
            {
                ProcessItem(item);
            }
        }

        private void ProcessItem(object target)
        {
            var floating = target as IMyEntity;
            if (floating == null)
                return;

            if (Sync.IsServer)
            {
                if (!IsEnabled())
                {
                    Logging.Instance.WriteLine("CANCELLING Cleanup Target due to being disabled");
                    CancelTarget(floating);
                    return;
                }

                if (m_constructionBlock.FactoryState != NaniteConstructionBlock.FactoryStates.Active)
                    return;

                if(!NaniteConstructionPower.HasRequiredPowerForCurrentTarget((IMyFunctionalBlock)m_constructionBlock.ConstructionBlock))
                {
                    Logging.Instance.WriteLine("CANCELLING Cleanup Target due to power shortage");
                    CancelTarget(floating);
                    return;
                }

                if(floating.Closed)
                {
                    CompleteTarget(floating);
                    return;
                }

                if (!m_targetTracker.ContainsKey(floating))
                {
                    m_constructionBlock.SendAddTarget(floating);
                }

                if(m_targetTracker.ContainsKey(floating))
                {
                    var trackedItem = m_targetTracker[floating];
                    if (MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds - trackedItem.StartTime >= trackedItem.CarryTime &&
                        MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds - trackedItem.LastUpdate > 2000)
                    {
                        trackedItem.LastUpdate = MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds;
                        if (!TransferFromTarget((IMyEntity)target))
                        {
                            Logging.Instance.WriteLine("CANCELLING Cleanup Target due to insufficient storage");
                            CancelTarget(floating);
                        }
                    }
                }
            }

            CreateFloatingParticle(floating);
        }

        private void OpenBag(IMyEntity bagEntity)
        {
            try
            {
                MyInventoryBagEntity bag = bagEntity as MyInventoryBagEntity;
                if (bag == null)
                    return;

                foreach (var item in bag.GetInventory().GetItems().ToList())
                {
                    MyFloatingObjects.Spawn(new MyPhysicalInventoryItem(item.Amount, item.Content), bagEntity.WorldMatrix.Translation, bagEntity.WorldMatrix.Forward, bagEntity.WorldMatrix.Up);
                }

                bagEntity.Close();
            }
            catch(Exception ex)
            {
                Logging.Instance.WriteLine(string.Format("OpenBag Error(): {0}", ex.ToString()));
            }
        }

        private void OpenCharacter(IMyEntity charEntity)
        {
            try
            {
                MyEntity entity = (MyEntity)charEntity;
                charEntity.Close();
            }
            catch(Exception ex)
            {
                Logging.Instance.WriteLine(string.Format("OpenCharacter() Error: {0}", ex.ToString()));
            }
        }

        private bool TransferFromTarget(IMyEntity target, bool transfer=true)
        {
            if(target is IMyCharacter)
            {
                if (transfer)
                {
                    MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                    {
                        OpenCharacter(target);
                    });
                }

                return true;
            }

            if(target is MyInventoryBagEntity)
            {
                if (transfer)
                {
                    MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                    {
                        OpenBag(target);
                    });
                }

                return true;
            }

            MyFloatingObject floating = (MyFloatingObject)target;
            MyInventory targetInventory = ((MyCubeBlock)m_constructionBlock.ConstructionBlock).GetInventory();
            var def = MyDefinitionManager.Static.GetPhysicalItemDefinition(new VRage.Game.MyDefinitionId(floating.Item.Content.TypeId, floating.Item.Content.SubtypeId));
            float amount = GetNaniteInventoryAmountThatFits(target, (MyCubeBlock)m_constructionBlock.ConstructionBlock);
            if ((int)amount > 0 || MyAPIGateway.Session.CreativeMode)
            {
                if (MyAPIGateway.Session.CreativeMode)
                    m_carryVolume = 10000f;

                if ((int)amount > 1 && (amount / def.Volume) > m_carryVolume)
                    amount = m_carryVolume / def.Volume;

                if ((int)amount < 1)
                    amount = 1f;

                if(transfer)
                    targetInventory.PickupItem(floating, (int)amount);

                return true;
            }

            return FindFreeCargo(target, (MyCubeBlock)m_constructionBlock.ConstructionBlock, transfer);
        }

        private bool FindFreeCargo(IMyEntity target, MyCubeBlock startBlock, bool transfer)
        {
            var list = Conveyor.GetConveyorListFromEntity(m_constructionBlock.ConstructionBlock);
            if (list == null)
                return false;

            List<MyInventory> inventoryList = new List<MyInventory>();
            foreach(var item in list)
            {
                IMyEntity entity;
                if (MyAPIGateway.Entities.TryGetEntityById(item, out entity))
                {
                    if (!(entity is IMyCubeBlock))
                        continue;

                    if (entity is Ingame.IMyRefinery || entity is Ingame.IMyAssembler)
                        continue;

                    MyCubeBlock block = (MyCubeBlock)entity;
                    if (!block.HasInventory)
                        continue;

                    inventoryList.Add(block.GetInventory());
                }
            }

            MyFloatingObject floating = (MyFloatingObject)target;
            float amount = 0f;
            MyInventory targetInventory = null;
            foreach (var item in inventoryList.OrderByDescending(x => (float)x.MaxVolume - (float)x.CurrentVolume))
            {
                amount = GetNaniteInventoryAmountThatFits(target, (MyCubeBlock)item.Owner);
                if ((int)amount == 0)
                    continue;

                targetInventory = item;
                break;
            }

            if ((int)amount == 0)
                return false;

            var def = MyDefinitionManager.Static.GetPhysicalItemDefinition(new VRage.Game.MyDefinitionId(floating.Item.Content.TypeId, floating.Item.Content.SubtypeId));
            if ((int)amount > 1 && (amount / def.Volume) > m_carryVolume)
                amount = m_carryVolume / def.Volume;

            if ((int)amount < 1)
                amount = 1f;

            if (transfer)
                targetInventory.PickupItem(floating, (int)amount);

            return true;
        }

        private float GetNaniteInventoryAmountThatFits(IMyEntity target, MyCubeBlock block)
        {
            if (!block.HasInventory)
                return 0f;

            MyFloatingObject floating = (MyFloatingObject)target;

            var def = MyDefinitionManager.Static.GetPhysicalItemDefinition(new VRage.Game.MyDefinitionId(floating.Item.Content.TypeId, floating.Item.Content.SubtypeId));
            MyInventory inventory = block.GetInventory();
            MyFixedPoint amountFits = inventory.ComputeAmountThatFits(new VRage.Game.MyDefinitionId(floating.Item.Content.TypeId, floating.Item.Content.SubtypeId));
            //Logging.Instance.WriteLine(string.Format("AmountFits: {0} - {1}", amountFits, def.Volume));
            //float amount;
            /*
            if (amountFits * def.Volume > 1)
            {
                amount = 1f / def.Volume;
            }
            else
                amount = (float)amountFits;
                */

            return (float)amountFits;
        }

        public void CancelTarget(IMyEntity obj)
        {
            Logging.Instance.WriteLine(string.Format("CANCELLING Floating Object Target: {0} - {1} (EntityID={2},Position={3})", m_constructionBlock.ConstructionBlock.EntityId, obj.GetType().Name, obj.EntityId, obj.GetPosition()));
            if (Sync.IsServer)
                m_constructionBlock.SendCancelTarget(obj);

            m_constructionBlock.ParticleManager.CancelTarget(obj);

            if (m_targetTracker.ContainsKey(obj))
                m_targetTracker.Remove(obj);

            Remove(obj);
        }

        public void CancelTarget(long entityId)
        {
            m_constructionBlock.ParticleManager.CancelTarget(entityId);
            foreach (var item in m_targetTracker.ToList())
            {
                if (item.Key.EntityId == entityId)
                {
                    m_targetTracker.Remove(item.Key);
                }
            }

            foreach (IMyEntity item in TargetList.Where(x => ((IMyEntity)x).EntityId == entityId))
                Logging.Instance.WriteLine(string.Format("COMPLETING Floating Object Target: {0} - {1} (EntityID={2},Position={3})", m_constructionBlock.ConstructionBlock.EntityId, item.EntityId, item.GetPosition()));

            TargetList.RemoveAll(x => ((IMyEntity)x).EntityId == entityId);
            PotentialTargetList.RemoveAll(x => ((IMyEntity)x).EntityId == entityId);            
        }

        public override void CancelTarget(object obj)
        {
            var target = obj as IMyEntity;
            if (target == null)
                return;

            CancelTarget(target);
        }

        public override void CompleteTarget(object obj)
        {
            var target = obj as IMyEntity;
            if (target == null)
                return;

            CompleteTarget(target);
        }

        public void CompleteTarget(IMyEntity obj)
        {
            Logging.Instance.WriteLine(string.Format("COMPLETING Floating Object Target: {0} - {1} (EntityID={2},Position={3})", m_constructionBlock.ConstructionBlock.EntityId, obj.GetType().Name, obj.EntityId, obj.GetPosition()));
            if (Sync.IsServer)
            {
                m_constructionBlock.SendCompleteTarget(obj);
            }

            m_constructionBlock.ParticleManager.CompleteTarget(obj);

            if(m_targetTracker.ContainsKey(obj))
                m_targetTracker.Remove(obj);

            Remove(obj);
        }

        public void CompleteTarget(long entityId)
        {
            m_constructionBlock.ParticleManager.CompleteTarget(entityId);
            foreach (var item in m_targetTracker.ToList())
            {
                if (item.Key.EntityId == entityId)
                {
                    m_targetTracker.Remove(item.Key);
                }
            }

            foreach(IMyEntity item in TargetList.Where(x => ((IMyEntity)x).EntityId == entityId))
                Logging.Instance.WriteLine(string.Format("COMPLETING Floating Object Target: {0} - {1} (EntityID={2},Position={3})", m_constructionBlock.ConstructionBlock.EntityId, item.GetType().Name, item.EntityId, item.GetPosition()));

            TargetList.RemoveAll(x => ((IMyEntity)x).EntityId == entityId);
            PotentialTargetList.RemoveAll(x => ((IMyEntity)x).EntityId == entityId);
        }

        private void CreateFloatingParticle(IMyEntity target)
        {
            double distance = Vector3D.Distance(m_constructionBlock.ConstructionBlock.GetPosition(), target.GetPosition());
            int time = (int)Math.Max(GetMinTravelTime() * 1000f, (distance / GetSpeed()) * 1000f);

            // This should be moved somewhere else.  It initializes a tracker
            if (!m_targetTracker.ContainsKey(target))
            {
                NaniteFloatingTarget floatingTarget = new NaniteFloatingTarget();
                floatingTarget.ParticleCount = 0;
                floatingTarget.StartTime = MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds;
                floatingTarget.LastUpdate = MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds;
                floatingTarget.CarryTime = time - 1000;
                m_targetTracker.Add(target, floatingTarget);
            }

            if (NaniteParticleManager.TotalParticleCount > NaniteParticleManager.MaxTotalParticles)
                return;

            m_targetTracker[target].ParticleCount++;
            int size = (int)Math.Max(60f, NaniteParticleManager.TotalParticleCount);
            if ((float)m_targetTracker[target].ParticleCount / size < 1f)
                return;

            m_targetTracker[target].ParticleCount = 0;

            // Create Particle
            Vector4 startColor = new Vector4(0.75f, 0.75f, 0.0f, 0.75f);
            Vector4 endColor = new Vector4(0.20f, 0.20f, 0.0f, 0.75f);
            m_constructionBlock.ParticleManager.AddParticle(startColor, endColor, GetMinTravelTime() * 1000f, GetSpeed(), target);
        }

        public override void ParallelUpdate(List<IMyCubeGrid> gridList, List<IMySlimBlock> blocks)
        {
            using (m_lock.AcquireExclusiveUsing())
            {
                PotentialTargetList.Clear();
            }

            m_entities.Clear();
            try
            {
                MyAPIGateway.Entities.GetEntities(m_entities, x => x is IMyFloatingObject || x is MyInventoryBagEntity || x is IMyCharacter);
            }
            catch
            {
                Logging.Instance.WriteLine(string.Format("Error getting entities, skipping"));
                return;
            }

            if (!IsEnabled())
                return;

            foreach (var item in m_entities)
            {
                if(item is IMyCharacter)
                {
                    var charBuilder = (MyObjectBuilder_Character)item.GetObjectBuilder();
                    if (charBuilder.LootingCounter <= 0f)
                        continue;
                }

                if(Vector3D.DistanceSquared(m_constructionBlock.ConstructionBlock.GetPosition(), item.GetPosition()) < m_maxDistance * m_maxDistance &&
                   TransferFromTarget(item, false))
                {
                    using(m_lock.AcquireExclusiveUsing())
                        PotentialTargetList.Add(item);
                }
            }
        }
    }
}
