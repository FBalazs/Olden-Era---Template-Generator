namespace Olden_Era___Template_Editor.Models
{
    public enum MapTopology
    {
        /// <summary>Zones are arranged in a circle; each connects to its two neighbours.</summary>
        Default,

        /// <summary>All zones connect to a shared central hub zone. Players never border each other.</summary>
        HubAndSpoke,

        /// <summary>Zones are connected in a straight line with no wrap-around.</summary>
        Chain,

        /// <summary>Players are leaf zones in a binary tree; internal nodes are neutral connector zones.</summary>
        BinaryTree,

        /// <summary>Players connect to shared neutral zones, which form a ring between them.</summary>
        SharedWeb,

        /// <summary>Zones are placed at random positions; each zone connects to all zones that border it based on proximity.</summary>
        Random,

        /// <summary>Hub-based layout where neutral zones are split evenly and placed only on player-to-hub spokes.</summary>
        HubAlternative
    }
}
