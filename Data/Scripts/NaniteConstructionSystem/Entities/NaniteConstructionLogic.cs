using Sandbox.Common.ObjectBuilders;
using VRage.Game.Components;
using VRage.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Game;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.Game;
using VRage.Game.ObjectBuilders;
using VRage.Utils;
using VRage;

namespace NaniteConstructionSystem.Entities
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_OxygenFarm), false, "LargeNaniteFactory")]
    public class NaniteConstructionLogic : MyGameLogicComponent
    {
        private NaniteConstructionBlock m_block;
        private static FastResourceLock m_lock = null;
        
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            if (m_lock == null)
                m_lock = new FastResourceLock();

            base.Init(objectBuilder);
            using (m_lock.AcquireExclusiveUsing())
            {
                if (!NaniteConstructionManager.NaniteBlocks.ContainsKey(Entity.EntityId))
                {
                    m_block = new NaniteConstructionBlock(Entity);
                    NaniteConstructionManager.NaniteBlocks.Add(Entity.EntityId, m_block);
                    IMySlimBlock slimBlock = ((MyCubeBlock)m_block.ConstructionBlock).SlimBlock as IMySlimBlock;
                    Logging.Instance.WriteLine(string.Format("ADDING Nanite Factory: conid={0} physics={1} ratio={2}", Entity.EntityId, m_block.ConstructionBlock.CubeGrid.Physics == null, slimBlock.BuildLevelRatio));

                    //if (NaniteConstructionManager.NaniteSync != null)
                    //    NaniteConstructionManager.NaniteSync.SendNeedTerminalSettings(Entity.EntityId);
                }
            }
        }

        /// <summary>
        /// GetObjectBuilder on a block is always null
        /// </summary>
        /// <param name="copy"></param>
        /// <returns></returns>
        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
        {
            return null;
        }

        public override void Close()
        {
            using (m_lock.AcquireExclusiveUsing())
            {
                if (NaniteConstructionManager.NaniteBlocks == null)
                    return;

                if (NaniteConstructionManager.NaniteBlocks.ContainsKey(Entity.EntityId))
                {
                    Logging.Instance.WriteLine(string.Format("REMOVING Nanite Factory: {0}", Entity.EntityId));
                    NaniteConstructionManager.NaniteBlocks[Entity.EntityId].Unload();
                    NaniteConstructionManager.NaniteBlocks.Remove(Entity.EntityId);
                }
            }
        }
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Projector), true)]
    public class NaniteProjectorLogic : MyGameLogicComponent
    {
        private static FastResourceLock m_lock = null;
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            if (m_lock == null)
                m_lock = new FastResourceLock();

            base.Init(objectBuilder);

            using (m_lock.AcquireExclusiveUsing())
            {
                if (!NaniteConstructionManager.ProjectorBlocks.ContainsKey(Entity.EntityId))
                {
                    NaniteConstructionManager.ProjectorBlocks.Add(Entity.EntityId, Entity as IMyCubeBlock);
                }
            }
        }

        /// <summary>
        /// GetObjectBuilder on a block is always null
        /// </summary>
        /// <param name="copy"></param>
        /// <returns></returns>
        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
        {
            return null;
        }

        public override void Close()
        {
            if (NaniteConstructionManager.ProjectorBlocks == null)
                return;

            if (NaniteConstructionManager.ProjectorBlocks.ContainsKey(Entity.EntityId))
            {
                NaniteConstructionManager.ProjectorBlocks.Remove(Entity.EntityId);
            }
        }
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Assembler), true)]
    public class NaniteAssemblerLogic : MyGameLogicComponent
    {
        private static FastResourceLock m_lock = null;
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            if (m_lock == null)
                m_lock = new FastResourceLock();

            base.Init(objectBuilder);

            using (m_lock.AcquireExclusiveUsing())
            {
                if (!NaniteConstructionManager.AssemblerBlocks.Contains(Entity.EntityId))
                {
                    NaniteConstructionManager.AssemblerBlocks.Add(Entity.EntityId);
                    if (NaniteConstructionManager.NaniteSync != null)
                        NaniteConstructionManager.NaniteSync.SendNeedAssemblerSettings(Entity.EntityId);
                }

                Logging.Instance.WriteLine($"Initializing NaniteAssemblerLogic for block: {Entity.EntityId}");
            }
        }

        /// <summary>
        /// GetObjectBuilder on a block is always null
        /// </summary>
        /// <param name="copy"></param>
        /// <returns></returns>
        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
        {
            return null;
        }

        public override void Close()
        {
            if (NaniteConstructionManager.AssemblerBlocks == null)
                return;

            if (NaniteConstructionManager.AssemblerBlocks.Contains(Entity.EntityId))
            {
                NaniteConstructionManager.AssemblerBlocks.Remove(Entity.EntityId);
            }
        }
    }
}
