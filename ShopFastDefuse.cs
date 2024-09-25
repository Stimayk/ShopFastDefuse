using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using ShopAPI;

namespace ShopFastDefuse
{
    public class ShopFastDefuse : BasePlugin
    {
        public override string ModuleName => "[SHOP] Fast Defuse";
        public override string ModuleDescription => "";
        public override string ModuleAuthor => "E!N";
        public override string ModuleVersion => "v1.0.1";

        private IShopApi? SHOP_API;
        private const string CategoryName = "FastDefuse";
        public static JObject? JsonFastDefuse { get; private set; }
        private readonly PlayerFastDefuse[] playerFastDefuse = new PlayerFastDefuse[65];

        public override void OnAllPluginsLoaded(bool hotReload)
        {
            SHOP_API = IShopApi.Capability.Get();
            if (SHOP_API == null) return;

            LoadConfig();
            InitializeShopItems();
            SetupTimersAndListeners();
        }

        private void LoadConfig()
        {
            string configPath = Path.Combine(ModuleDirectory, "../../configs/plugins/Shop/FastDefuse.json");
            if (File.Exists(configPath))
            {
                JsonFastDefuse = JObject.Parse(File.ReadAllText(configPath));
            }
        }

        private void InitializeShopItems()
        {
            if (JsonFastDefuse == null || SHOP_API == null) return;

            SHOP_API.CreateCategory(CategoryName, "Быстрое разминирование");

            var sortedItems = JsonFastDefuse
                .Properties()
                .Select(p => new { Key = p.Name, Value = (JObject)p.Value })
                .OrderBy(p => (float)p.Value["speed"]!)
                .ToList();

            foreach (var item in sortedItems)
            {
                Task.Run(async () =>
                {
                    int itemId = await SHOP_API.AddItem(item.Key, (string)item.Value["name"]!, CategoryName, (int)item.Value["price"]!, (int)item.Value["sellprice"]!, (int)item.Value["duration"]!);
                    SHOP_API.SetItemCallbacks(itemId, OnClientBuyItem, OnClientSellItem, OnClientToggleItem);
                }).Wait();
            }
        }

        private void SetupTimersAndListeners()
        {
            RegisterListener<Listeners.OnClientDisconnect>(playerSlot => playerFastDefuse[playerSlot] = null!);

            RegisterEventHandler<EventBombBegindefuse>((@event, info) =>
            {
                var player = @event.Userid;
                if (player == null || playerFastDefuse[player.Slot] == null) return HookResult.Continue;

                var playerPawn = player.PlayerPawn.Value;
                if (playerPawn == null || !player.PawnIsAlive) return HookResult.Continue;

                var featureValue = playerFastDefuse[player.Slot].Speed;
                var bomb = Utilities.FindAllEntitiesByDesignerName<CPlantedC4>("planted_c4").First();

                Server.NextFrame(() =>
                {
                    float countDown;
                    if (bomb.DefuseCountDown < Server.CurrentTime)
                        countDown = 10;
                    else
                        countDown = bomb.DefuseCountDown - Server.CurrentTime;

                    countDown -= countDown / 100 * featureValue;
                    bomb.DefuseCountDown = countDown + Server.CurrentTime;
                    playerPawn.ProgressBarDuration = (int)float.Ceiling(countDown);
                });

                return HookResult.Continue;
            });
        }

        public HookResult OnClientBuyItem(CCSPlayerController player, int itemId, string categoryName, string uniqueName, int buyPrice, int sellPrice, int duration, int count)
        {
            if (TryGetDefuseSpeed(uniqueName, out float speed))
            {
                playerFastDefuse[player.Slot] = new PlayerFastDefuse(speed, itemId);
            }
            else
            {
                Logger.LogError($"{uniqueName} has invalid or missing 'speed' in config!");
            }
            return HookResult.Continue;
        }

        public HookResult OnClientToggleItem(CCSPlayerController player, int itemId, string uniqueName, int state)
        {
            if (state == 1 && TryGetDefuseSpeed(uniqueName, out float speed))
            {
                playerFastDefuse[player.Slot] = new PlayerFastDefuse(speed, itemId);
            }
            else if (state == 0)
            {
                OnClientSellItem(player, itemId, uniqueName, 0);
            }
            return HookResult.Continue;
        }

        public HookResult OnClientSellItem(CCSPlayerController player, int itemId, string uniqueName, int sellPrice)
        {
            playerFastDefuse[player.Slot] = null!;
            return HookResult.Continue;
        }

        private static bool TryGetDefuseSpeed(string uniqueName, out float speed)
        {
            speed = 0;
            if (JsonFastDefuse != null && JsonFastDefuse.TryGetValue(uniqueName, out var obj) && obj is JObject jsonItem && jsonItem["speed"] != null && jsonItem["speed"]!.Type != JTokenType.Null)
            {
                speed = float.Parse(jsonItem["speed"]!.ToString());
                return true;
            }
            return false;
        }

        public record class PlayerFastDefuse(float Speed, int ItemID);
    }
}