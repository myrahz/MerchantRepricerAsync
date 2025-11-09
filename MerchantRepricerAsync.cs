using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.Shared;
using ExileCore.Shared.Helpers;
using SharpDX;
using Vector2 = System.Numerics.Vector2;

namespace MerchantRepricerAsync
{
    public class MerchantRepricerAsync : BaseSettingsPlugin<MerchantRepricerAsyncSettings>
    {
        private bool _isProcessing = false;
        private InputHandler _inputHandler;
        private CancellationTokenSource _cancellationTokenSource;
        private SyncTask<bool> _repricerTask;

        public override bool Initialise()
        {
            var windowPosition = GameController.Window.GetWindowRectangleTimeCache.TopLeft;
            var windowFix = new Vector2(windowPosition.X, windowPosition.Y);
            _inputHandler = new InputHandler(windowFix, Settings);
            _cancellationTokenSource = new CancellationTokenSource();
            return true;
        }

        public override void Render()
        {
            // Toggle async task via hotkey
            if (Settings.Hotkey.PressedOnce())
            {
                _isProcessing = !_isProcessing;

                if (_isProcessing)
                {
                    LogMessage("Merchant Repricer started.", 5, Color.LimeGreen);
                    //_cancellationTokenSource?.Cancel();
                    //_cancellationTokenSource?.Dispose();
                    _cancellationTokenSource = new CancellationTokenSource();
                }
                else
                {
                    LogMessage("Merchant Repricer stopped.", 5, Color.Yellow);
                    _cancellationTokenSource?.Cancel();
                    _cancellationTokenSource?.Dispose();
                    _repricerTask = null;
                }
            }
            if (_isProcessing)
            {
                //_repricerTask = null;
                TaskUtils.RunOrRestart(ref _repricerTask, () => RunMerchantRepricerAsync(_cancellationTokenSource.Token));
            }
        }



        private async SyncTask<bool> RunMerchantRepricerAsync(CancellationToken token)
        {
            LogMessage("Entered RunMerchantRepricerAsync", 5, Color.Yellow);
            try
            {
                var ingameState = GameController.Game.IngameState;
                var merchantWindow = ingameState.IngameUi.OfflineMerchantPanel;

                if (!merchantWindow.IsVisible)
                {
                    LogMessage("Merchant window not visible. Open it to start repricing.", 5, Color.Red);
                    _isProcessing = false;
                    return false;
                }

                // Collect merchant tab coordinates
                var tabs = merchantWindow.VisibleStash?.NestedTabSwitchBar?.Children;
                if (tabs == null || tabs.Count == 0)
                {
                    LogMessage("No merchant tabs found.", 5, Color.Red);
                    _isProcessing = false;
                    return false;
                }

                List<SharpDX.RectangleF> tabRects = new List<SharpDX.RectangleF>();
                foreach (var tab in tabs)
                {
                    if (tab.ChildCount > 0)
                        tabRects.Add(tab.GetClientRect());
                }

                var inventoryRect = ingameState.IngameUi.OfflineMerchantPanel.VisibleStash.GetChildFromIndices(1, 1).GetClientRect();

                Graphics.DrawFrame(inventoryRect, SharpDX.Color.Red, 5);
                inventoryRect.X += 2.5f;
                inventoryRect.Y += 2.5f;
                inventoryRect.Width -= 6f;
                inventoryRect.Height -= 6f;

                float squareWidth = inventoryRect.Width / 12f;
                float squareHeight = inventoryRect.Height / 12f;

                int tabIndex = 0;

                var repricePercentDouble = ((double)Settings.RepricePercent / 100);
                LogMessage($"repricePercentDouble  {repricePercentDouble}", 5, Color.Orange);

                foreach (var tabRect in tabRects)
                {
                    token.ThrowIfCancellationRequested();
                    LogMessage($"Clicking tab {tabIndex}", 5, Color.Orange);

                    await _inputHandler.MoveCursorAndClick(new Vector2(tabRect.Center.X, tabRect.Center.Y), token);
                    await Task.Delay(Settings.InputDelay + 200, token);

                    // Refresh memory after clicking
                    ingameState = GameController.Game.IngameState;
                    merchantWindow = ingameState.IngameUi.OfflineMerchantPanel;

                    var stash = merchantWindow.VisibleStash;
                    var items = stash?.ServerInventory?.InventorySlotItems;
                    if (items == null || items.Count == 0)
                    {
                        LogMessage($"No items in tab {tabIndex}", 5, Color.Gray);
                        tabIndex++;
                        continue;
                    }

                    // Snapshot items
                    var safeItems = items
                        .Where(i => i?.Item != null)
                        .Select(i => new
                        {
                            X = i.PosX,
                            Y = i.PosY,
                            W = i.SizeX,
                            H = i.SizeY,
                            Price = i.Item.GetComponent<Base>()?.PublicPrice ?? ""
                        })
                        .ToList();

                    LogMessage($"Tab {tabIndex}: {safeItems.Count} items found.", 5, Color.Cyan);

                    foreach (var item in safeItems)
                    {
                        token.ThrowIfCancellationRequested();

                        float x = inventoryRect.X + (item.X * squareWidth);
                        float y = inventoryRect.Y + (item.Y * squareHeight);
                        float w = squareWidth * item.W;
                        float h = squareHeight * item.H;
                        var drawRect = new SharpDX.RectangleF(x, y, w, h);
                        Graphics.DrawFrame(drawRect, SharpDX.Color.Red, 2);
                        Graphics.DrawFrame(inventoryRect, SharpDX.Color.Red, 5);
                        if (string.IsNullOrWhiteSpace(item.Price))
                        {
                            LogMessage("Item has no price.", 5, Color.Gray);
                            continue;
                        }

                        string pattern = @"~b/o\s+(\d+(?:\.\d+)?)\s+(\w+)";
                        Match match = Regex.Match(item.Price, pattern);
                        if (match.Success)
                        {
                            double amount = Math.Round(double.Parse(match.Groups[1].Value), MidpointRounding.AwayFromZero); 
                            string currency = match.Groups[2].Value;
                            double newAmount = Math.Round(amount * repricePercentDouble, MidpointRounding.AwayFromZero);
                            
                            
                            // click the item
                            if (amount != newAmount && (currency == "chaos" || Settings.RepriceOtherThanChaos))
                            {
                                LogMessage($"Repriced {amount} {currency} → {newAmount} {currency}", 5, Color.LimeGreen);
                                await Task.Delay(Settings.ItemDelay, token);
                                //await _inputHandler.MoveCursor(new Vector2(drawRect.Center.X, drawRect.Center.Y), token);
                                await _inputHandler.MoveCursorAndRightClick(new Vector2(drawRect.Center.X, drawRect.Center.Y), token);
                                await Task.Delay(Settings.ItemDelay, token);
                                await Task.Delay(Settings.InputDelay, token);

                                if (!ingameState.IngameUi.PopUpWindow.IsVisible)
                                {
                                    LogMessage("Couldnt price as window didnt popup, skipping", 5, Color.Gray);
                                    continue;
                                }
                                // press the number 
                                string newAmountText = ((int)newAmount).ToString();  // ensure integer (no decimals)
                                if (Settings.DebugMode) 
                                    LogMessage($"newAmountText {newAmountText}", 5, Color.LimeGreen);
                                foreach (char c in newAmountText)
                                {
                                    if (char.IsDigit(c))
                                    {
                                        // Convert char to virtual key (e.g. '3' -> Keys.D3)
                                        var key = (Keys)Enum.Parse(typeof(Keys), "D" + c);
                                        if(Settings.DebugMode)
                                            LogMessage($"pressing {key.ToString()}", 5, Color.LimeGreen);
                                        try
                                        {
                                            Input.KeyDown(key);
                                        }
                                        finally
                                        {
                                            Input.KeyUp(key);
                                        }
                                        await Task.Delay(Settings.ItemDelay, token);  // small delay between key presses
                                    }
                                }
                                // press enter
                                await _inputHandler.PressEnterKey(token);

                                await Task.Delay(Settings.InputDelay, token);
                            }else  if (currency == "chaos" && newAmount == amount && amount > 1)
                            {
                                newAmount = amount - 1;
                                LogMessage($"Chaos price adjusted manually from {amount} → {newAmount}", 5, Color.Orange);
                                await Task.Delay(Settings.ItemDelay, token);
                                //await _inputHandler.MoveCursor(new Vector2(drawRect.Center.X, drawRect.Center.Y), token);
                                await _inputHandler.MoveCursorAndRightClick(new Vector2(drawRect.Center.X, drawRect.Center.Y), token);
                                if (!ingameState.IngameUi.PopUpWindow.IsVisible)
                                {
                                    LogMessage("Couldnt price as window didnt popup, skipping", 5, Color.Gray);
                                    continue;
                                }
                                await Task.Delay(Settings.ItemDelay, token);
                                await Task.Delay(Settings.InputDelay, token);
                                // press the number 
                                string newAmountText = ((int)newAmount).ToString();  // ensure integer (no decimals)
                                if (Settings.DebugMode) 
                                    LogMessage($"newAmountText {newAmountText}", 5, Color.LimeGreen);
                                foreach (char c in newAmountText)
                                {
                                    if (char.IsDigit(c))
                                    {
                                        // Convert char to virtual key (e.g. '3' -> Keys.D3)
                                        var key = (Keys)Enum.Parse(typeof(Keys), "D" + c);
                                        if (Settings.DebugMode) 
                                            LogMessage($"pressing {key.ToString()}", 5, Color.LimeGreen);
                                        try
                                        {
                                            Input.KeyDown(key);
                                        }
                                        finally
                                        {
                                            Input.KeyUp(key);
                                        }
                                        await Task.Delay(Settings.ItemDelay, token);  // small delay between key presses
                                    }
                                }
                                // press enter
                                await _inputHandler.PressEnterKey(token);

                                await Task.Delay(Settings.InputDelay, token);
                                


                            }
                            else
                            {
                                LogMessage($"Same price {amount} {currency} → {newAmount} {currency}", 5, Color.Red);
                            }
                           


                        }
                        else
                        {
                            LogMessage("Failed to parse price format.", 5, Color.Red);
                        }

                        await Task.Delay(Settings.InputDelay, token);
                    }

                    tabIndex++;
                }

                LogMessage("Merchant Repricer finished all tabs.", 5, Color.LimeGreen);
                _isProcessing = false;
                return true;
            }
            catch (OperationCanceledException)
            {
                LogMessage("Merchant Repricer cancelled.", 5, Color.Yellow);
                _isProcessing = false;
                return false;
            }
            catch (Exception ex)
            {
                LogError($"Merchant Repricer failed: {ex.Message}");
                _isProcessing = false;
                return false;
            }
        }
    }
}
