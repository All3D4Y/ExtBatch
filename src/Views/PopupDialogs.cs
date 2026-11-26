using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CmOxeProvisioner.Services;

namespace CmOxeProvisioner.Views
{
    public static class PopupDialogs
    {
        public static void ShowSyntaxToast(Window owner, bool isSuccess, string title, string message)
        {
            StackPanel shell;
            Window dlg = CreateThemedDialogShell(owner, title ?? "알림", 440, out shell);

            StackPanel root = new StackPanel();
            root.Margin = new Thickness(18);
            root.Children.Add(BuildAlertHeader(isSuccess ? "Ok" : "Err", isSuccess ? "✓" : "!", title, 13.5, message, null));

            StackPanel btnRow = new StackPanel();
            btnRow.Orientation = Orientation.Horizontal;
            btnRow.HorizontalAlignment = HorizontalAlignment.Right;
            btnRow.Margin = new Thickness(0, 16, 0, 0);

            Button btnOk = new Button();
            btnOk.Content = "확인";
            btnOk.Style = Application.Current.Resources["PrimaryBtn"] as Style;
            btnOk.MinWidth = 86;
            btnOk.IsDefault = true;
            btnOk.Click += delegate { dlg.DialogResult = true; dlg.Close(); };
            btnRow.Children.Add(btnOk);

            root.Children.Add(btnRow);
            shell.Children.Add(root);

            ShowThemedModal(owner, dlg, btnOk, true, true);
        }

        public static bool ConfirmLargeExtensionWarning(Window owner, int dnCount, int threshold)
        {
            string detail = string.Format("현재 요청된 내선번호는 {0}개입니다 (주의 기준: {1}개 이상).\n계속 진행하시겠습니까?", dnCount, threshold);
            return ShowThemedConfirm(
                owner,
                "대량 내선번호 생성 주의",
                "한 번에 많은 내선번호를 생성하는 것은 주의가 필요합니다!",
                detail,
                false,
                "계속 진행");
        }

        public static bool ConfirmDelete(Window owner, string windowTitle, string headline, string detail)
        {
            return ShowDeleteTypedConfirm(owner, windowTitle, headline, detail, null);
        }

        public static bool ConfirmDelete(Window owner, string windowTitle, string headline, string detail, string customWarningDetail)
        {
            return ShowDeleteTypedConfirm(owner, windowTitle, headline, detail, customWarningDetail);
        }

        public static bool ConfirmAction(Window owner, string windowTitle, string headline, string detail, string confirmText)
        {
            return ShowThemedConfirm(owner, windowTitle, headline, detail, false, confirmText);
        }

        private static Brush ResBrush(string key, Brush fallback)
        {
            return (Application.Current.Resources[key] as Brush) ?? fallback;
        }

        private static Window CreateThemedDialogShell(Window owner, string windowTitle, double width, out StackPanel shell)
        {
            Window dlg = new Window();
            dlg.Title = windowTitle;
            dlg.Width = width;
            dlg.SizeToContent = SizeToContent.Height;
            dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            dlg.ResizeMode = ResizeMode.NoResize;
            dlg.ShowInTaskbar = false;
            if (owner != null && owner.IsVisible) dlg.Owner = owner;

            System.Windows.Shell.WindowChrome chrome = new System.Windows.Shell.WindowChrome();
            chrome.CaptionHeight = 34;
            chrome.ResizeBorderThickness = new Thickness(0);
            chrome.GlassFrameThickness = new Thickness(0);
            chrome.UseAeroCaptionButtons = false;
            System.Windows.Shell.WindowChrome.SetWindowChrome(dlg, chrome);

            dlg.Background = ResBrush("SurfaceBrush", Brushes.Black);
            dlg.Foreground = ResBrush("TextBrush", Brushes.White);

            Border outerFrame = new Border();
            outerFrame.BorderBrush = ResBrush("BorderBrush", Brushes.Gray);
            outerFrame.BorderThickness = new Thickness(1);
            outerFrame.Background = ResBrush("SurfaceBrush", Brushes.Black);
            KeyboardNavigation.SetTabNavigation(outerFrame, KeyboardNavigationMode.Cycle);
            KeyboardNavigation.SetControlTabNavigation(outerFrame, KeyboardNavigationMode.Cycle);
            KeyboardNavigation.SetDirectionalNavigation(outerFrame, KeyboardNavigationMode.Cycle);

            shell = new StackPanel();

            Border titleBar = new Border();
            titleBar.Background = ResBrush("HeaderBgBrush", Brushes.Black);
            titleBar.BorderBrush = ResBrush("BorderBrush", Brushes.Gray);
            titleBar.BorderThickness = new Thickness(0, 0, 0, 1);
            titleBar.Height = 34;
            DockPanel titleDock = new DockPanel();
            titleDock.LastChildFill = false;

            TextBlock titleTxt = new TextBlock();
            titleTxt.Text = windowTitle;
            titleTxt.FontSize = 12;
            titleTxt.FontWeight = FontWeights.SemiBold;
            titleTxt.Foreground = ResBrush("TextBrush", Brushes.White);
            titleTxt.VerticalAlignment = VerticalAlignment.Center;
            titleTxt.Margin = new Thickness(12, 0, 0, 0);
            DockPanel.SetDock(titleTxt, Dock.Left);
            titleDock.Children.Add(titleTxt);

            Button btnWinClose = new Button();
            btnWinClose.Style = Application.Current.Resources["CaptionCloseBtn"] as Style;
            btnWinClose.Focusable = false;
            btnWinClose.IsTabStop = false;
            System.Windows.Shapes.Path xPath = new System.Windows.Shapes.Path();
            xPath.Data = Geometry.Parse("M 0,0 L 9,9 M 9,0 L 0,9");
            xPath.StrokeThickness = 1.4;
            xPath.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1) });
            btnWinClose.Content = xPath;
            btnWinClose.Click += delegate { dlg.DialogResult = false; dlg.Close(); };
            btnWinClose.HorizontalAlignment = HorizontalAlignment.Right;
            DockPanel.SetDock(btnWinClose, Dock.Right);
            titleDock.Children.Add(btnWinClose);

            titleBar.Child = titleDock;
            shell.Children.Add(titleBar);

            outerFrame.Child = shell;
            dlg.Content = outerFrame;
            return dlg;
        }

        private static void CloseDialogDeferred(Window dlg, bool result)
        {
            if (dlg == null) return;
            dlg.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(delegate
            {
                if (dlg.IsLoaded)
                {
                    try { dlg.DialogResult = result; } catch { }
                    try { dlg.Close(); } catch { }
                }
            }));
        }

        private static bool ShowThemedModal(Window owner, Window dlg, IInputElement initialFocus, bool allowXKeyClose, bool xKeyDialogResult)
        {
            IInputElement prevFocus = (owner != null)
                ? (FocusManager.GetFocusedElement(owner) ?? Keyboard.FocusedElement)
                : Keyboard.FocusedElement;

            dlg.Loaded += delegate
            {
                dlg.Activate();
                if (initialFocus != null)
                {
                    initialFocus.Focus();
                    Keyboard.Focus(initialFocus);
                }
            };

            dlg.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    CloseDialogDeferred(dlg, false);
                    return;
                }
                if (allowXKeyClose && e.Key == Key.X && Keyboard.Modifiers == ModifierKeys.None && !(Keyboard.FocusedElement is TextBox))
                {
                    e.Handled = true;
                    CloseDialogDeferred(dlg, xKeyDialogResult);
                }
            };

            bool? res = dlg.ShowDialog();

            if (owner != null && prevFocus != null)
            {
                UIElement prevEl = prevFocus as UIElement;
                owner.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate
                {
                    try
                    {
                        if (prevEl == null || (prevEl.IsVisible && prevEl.IsEnabled))
                        {
                            Keyboard.Focus(prevFocus);
                        }
                    }
                    catch { }
                }));
            }

            return (res == true);
        }

        private static Border MakePillBadge(string text, string bgKey, string fgKey, string borderKey, bool bold, bool monospace, Thickness margin)
        {
            Border b = new Border();
            b.Background = ResBrush(bgKey, Brushes.DarkSlateGray);
            if (!string.IsNullOrEmpty(borderKey))
            {
                b.BorderBrush = ResBrush(borderKey, Brushes.Gray);
                b.BorderThickness = new Thickness(1);
            }
            b.CornerRadius = new CornerRadius(5);
            b.Padding = new Thickness(8, 3, 8, 3);
            b.Margin = margin;
            b.VerticalAlignment = VerticalAlignment.Center;

            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontSize = 11.5;
            t.FontWeight = bold ? FontWeights.Bold : FontWeights.SemiBold;
            if (monospace) t.FontFamily = new FontFamily("Consolas");
            t.Foreground = ResBrush(fgKey, Brushes.White);
            b.Child = t;
            return b;
        }

        private static string FormatMatchLocation(string rawMatch, string queryName)
        {
            if (string.IsNullOrWhiteSpace(rawMatch)) return "위치 정보 없음 (CM 목록에 존재)";
            string[] cols = rawMatch.Split('|');
            List<string> paths = new List<string>();
            List<string> extras = new List<string>();
            for (int i = 0; i < cols.Length; i++)
            {
                string part = (cols[i] ?? "").Trim();
                if (part.Length == 0 || string.Equals(part, queryName, StringComparison.OrdinalIgnoreCase)) continue;
                if (part.IndexOf('\\') >= 0 || part.IndexOf('/') >= 0) paths.Add(part);
                else extras.Add(part);
            }
            if (paths.Count > 0)
            {
                return string.Join(" / ", paths.ToArray());
            }
            if (extras.Count > 0) return string.Join(" · ", extras.ToArray());
            return rawMatch;
        }

        private static void AddCleanNumberGroup(StackPanel container, string label, List<string> nums, Thickness bottomMargin)
        {
            TextBlock lbl = new TextBlock();
            lbl.Text = string.Format("{0} ({1}건)", label, nums.Count);
            lbl.FontSize = 11.5;
            lbl.FontWeight = FontWeights.Bold;
            lbl.Foreground = ResBrush("OkFgBrush", Brushes.LightGreen);
            lbl.Margin = new Thickness(0, 0, 0, 5);
            container.Children.Add(lbl);

            if (nums.Count > 0)
            {
                WrapPanel pills = new WrapPanel();
                pills.Margin = bottomMargin;
                for (int i = 0; i < nums.Count; i++)
                {
                    pills.Children.Add(MakePillBadge(nums[i], "OkBgBrush", "OkFgBrush", "OkBorderBrush", true, true, new Thickness(0, 0, 6, 5)));
                }
                container.Children.Add(pills);
            }
            else
            {
                TextBlock none = new TextBlock();
                none.Text = string.Format("사용 가능한 {0} 없음", label);
                none.FontSize = 11.5;
                none.Foreground = ResBrush("MutedBrush", Brushes.Gray);
                none.Margin = bottomMargin;
                container.Children.Add(none);
            }
        }

        private static CmFindResultItem FindQueryResult(Dictionary<string, CmFindResultItem> lookup, string category, string name)
        {
            CmFindResultItem item;
            return lookup.TryGetValue(category + "\n" + name, out item) ? item : null;
        }

        private static Border MakeDupCardContainer(StackPanel cardBody)
        {
            Border card = new Border();
            card.Background = ResBrush("SurfaceAltBrush", Brushes.Black);
            card.BorderBrush = ResBrush("ErrBorderBrush", Brushes.IndianRed);
            card.BorderThickness = new Thickness(1);
            card.CornerRadius = new CornerRadius(6);
            card.Padding = new Thickness(11, 9, 11, 9);
            card.Margin = new Thickness(0, 0, 0, 7);
            card.Child = cardBody;
            return card;
        }

        private static DockPanel MakeDupCardHeader(string kindLabel, string number, Border optionalRightBadge)
        {
            DockPanel cardHead = new DockPanel();
            cardHead.LastChildFill = false;
            cardHead.Margin = new Thickness(0, 0, 0, 6);

            StackPanel leftHead = new StackPanel();
            leftHead.Orientation = Orientation.Horizontal;
            leftHead.VerticalAlignment = VerticalAlignment.Center;
            leftHead.Children.Add(MakePillBadge(kindLabel, "ChipBgBrush", "ChipFgBrush", null, true, false, new Thickness(0, 0, 8, 0)));

            TextBlock numTxt = new TextBlock();
            numTxt.Text = number;
            numTxt.FontFamily = new FontFamily("Consolas");
            numTxt.FontSize = 13.5;
            numTxt.FontWeight = FontWeights.Bold;
            numTxt.Foreground = ResBrush("TextBrush", Brushes.White);
            numTxt.VerticalAlignment = VerticalAlignment.Center;
            leftHead.Children.Add(numTxt);
            DockPanel.SetDock(leftHead, Dock.Left);
            cardHead.Children.Add(leftHead);

            if (optionalRightBadge != null)
            {
                DockPanel.SetDock(optionalRightBadge, Dock.Right);
                cardHead.Children.Add(optionalRightBadge);
            }
            return cardHead;
        }

        private static Border MakeErrHitRow(Thickness margin)
        {
            Border hitRow = new Border();
            hitRow.Background = ResBrush("ErrBgBrush", Brushes.DarkRed);
            hitRow.BorderBrush = ResBrush("ErrBorderBrush", Brushes.IndianRed);
            hitRow.BorderThickness = new Thickness(1);
            hitRow.CornerRadius = new CornerRadius(5);
            hitRow.Padding = new Thickness(8, 5, 8, 5);
            hitRow.Margin = margin;
            return hitRow;
        }

        public static void ShowCmCheckResult(Window owner, CmCheckSummary summary)
        {
            if (summary == null) return;

            Dictionary<string, CmFindResultItem> lookup = new Dictionary<string, CmFindResultItem>(StringComparer.OrdinalIgnoreCase);
            if (summary.FindResults != null)
            {
                for (int i = 0; i < summary.FindResults.Count; i++)
                {
                    CmFindResultItem item = summary.FindResults[i];
                    if (item == null) continue;
                    string key = (item.Category ?? "") + "\n" + (item.Name ?? "");
                    if (!lookup.ContainsKey(key)) lookup[key] = item;
                }
            }

            List<string> dnList = summary.DnList ?? new List<string>();
            List<string> ctiList = summary.CtiList ?? new List<string>();
            List<string> missingFolders = summary.MissingFolder ?? new List<string>();
            bool checkDn = dnList.Count > 0 && (summary.DoAgentDn || summary.DoPlace);
            bool checkCti = ctiList.Count > 0 && (summary.DoAgentLogin || summary.DoAcdPos);

            List<Border> dupCards = new List<Border>();
            List<string> cleanDns = new List<string>();
            List<string> cleanCtis = new List<string>();
            int dupNumberCount = 0;
            int dupObjectCount = 0;
            StringBuilder copyText = new StringBuilder();
            copyText.AppendLine(string.IsNullOrWhiteSpace(summary.Customer)
                ? "[CM 내선/CTI ID 검색 결과] CM 전역 검색"
                : string.Format("[CM 내선/CTI ID 검색 결과] 고객사: {0}", summary.Customer));

            if (missingFolders.Count > 0)
            {
                copyText.AppendLine("[대상 폴더 확인 필요]");
                for (int i = 0; i < missingFolders.Count; i++) copyText.AppendLine(" - " + missingFolders[i]);
            }

            Action<string, string, List<string>, string[], string[]> collectGroup = delegate(
                string groupTag,
                string displayPrefix,
                List<string> nums,
                string[] categories,
                string[] labels)
            {
                for (int i = 0; i < nums.Count; i++)
                {
                    string num = nums[i];
                    List<CmFindResultItem> hits = new List<CmFindResultItem>();
                    List<string> hitLabels = new List<string>();
                    List<string> cleanCats = new List<string>();

                    for (int k = 0; k < categories.Length; k++)
                    {
                        if (string.IsNullOrEmpty(categories[k])) continue;
                        string qName = (categories[k] == "Place") ? ("P" + num) : num;
                        CmFindResultItem fr = FindQueryResult(lookup, categories[k], qName);
                        if (fr != null && fr.Count > 0)
                        {
                            hits.Add(fr);
                            hitLabels.Add(labels[k]);
                            dupObjectCount += fr.Count;
                        }
                        else
                        {
                            cleanCats.Add(labels[k]);
                        }
                    }

                    if (hits.Count == 0)
                    {
                        if (groupTag == "DN") cleanDns.Add(num);
                        else cleanCtis.Add(num);
                        continue;
                    }

                    dupNumberCount++;
                    copyText.AppendLine(string.Format("[사용 중] {0} {1}", displayPrefix, num));

                    Border rightBadge = cleanCats.Count > 0
                        ? MakePillBadge("미사용: " + string.Join(", ", cleanCats.ToArray()), "OkBgBrush", "OkFgBrush", "OkBorderBrush", false, false, new Thickness(8, 0, 0, 0))
                        : null;

                    StackPanel cardBody = new StackPanel();
                    cardBody.Children.Add(MakeDupCardHeader(displayPrefix, num, rightBadge));

                    for (int h = 0; h < hits.Count; h++)
                    {
                        CmFindResultItem fr = hits[h];
                        Border hitRow = MakeErrHitRow(new Thickness(0, h == 0 ? 0 : 5, 0, 0));

                        StackPanel hitStack = new StackPanel();
                        string objLabel = (fr.Category == "Place") ? string.Format("Place ({0})", fr.Name) : hitLabels[h];
                        TextBlock hitHeader = new TextBlock();
                        hitHeader.Text = string.Format("{0} · {1}건 사용 중", objLabel, fr.Count);
                        hitHeader.FontSize = 11.5;
                        hitHeader.FontWeight = FontWeights.Bold;
                        hitHeader.Foreground = ResBrush("ErrFgBrush", Brushes.LightCoral);
                        hitStack.Children.Add(hitHeader);

                        List<string> matches = fr.Matches ?? new List<string>();
                        if (matches.Count == 0)
                        {
                            TextBlock locFallback = new TextBlock();
                            locFallback.Text = "위치: (CM 전역 검색 결과 존재)";
                            locFallback.FontSize = 11.5;
                            locFallback.Foreground = ResBrush("TextBrush", Brushes.White);
                            locFallback.Margin = new Thickness(0, 2, 0, 0);
                            hitStack.Children.Add(locFallback);
                            copyText.AppendLine(string.Format("  - {0}: 위치 미기재", objLabel));
                        }
                        else
                        {
                            for (int m = 0; m < matches.Count; m++)
                            {
                                string loc = FormatMatchLocation(matches[m], fr.Name);
                                TextBlock locTxt = new TextBlock();
                                locTxt.Text = "위치: " + loc;
                                locTxt.FontFamily = new FontFamily("Consolas");
                                locTxt.FontSize = 11.5;
                                locTxt.Foreground = ResBrush("TextBrush", Brushes.White);
                                locTxt.TextWrapping = TextWrapping.Wrap;
                                locTxt.Margin = new Thickness(0, 2, 0, 0);
                                hitStack.Children.Add(locTxt);
                                copyText.AppendLine(string.Format("  - {0} -> {1}", objLabel, loc));
                            }
                        }
                        hitRow.Child = hitStack;
                        cardBody.Children.Add(hitRow);
                    }

                    dupCards.Add(MakeDupCardContainer(cardBody));
                }
            };

            if (checkDn)
            {
                collectGroup(
                    "DN",
                    "내선",
                    dnList,
                    new string[] { summary.DoAgentDn ? "Agent DN" : "", summary.DoPlace ? "Place" : "" },
                    new string[] { "Agent DN", "Place" });
            }
            if (checkCti)
            {
                collectGroup(
                    "CTI",
                    "CTI",
                    ctiList,
                    new string[] { summary.DoAgentLogin ? "Agent Login" : "", summary.DoAcdPos ? "ACD Position (DN)" : "" },
                    new string[] { "Agent Login", "ACD Position" });
            }

            if (cleanDns.Count > 0) copyText.AppendLine("[미사용 - 내선] " + string.Join(", ", cleanDns.ToArray()));
            if (cleanCtis.Count > 0) copyText.AppendLine("[미사용 - CTI] " + string.Join(", ", cleanCtis.ToArray()));

            bool hasDups = dupCards.Count > 0;
            bool hasMissing = missingFolders.Count > 0;
            string statusTone = hasDups ? "Err" : (hasMissing ? "Warn" : "Ok");
            string winTitle = hasDups
                ? "CM 내선/CTI ID 검색 결과 — 사용 중 발견"
                : (hasMissing ? "CM 내선/CTI ID 검색 결과 — 폴더 확인 필요" : "CM 내선/CTI ID 검색 결과 — 사용 가능");
            string headline;
            if (hasDups)
            {
                headline = string.IsNullOrWhiteSpace(summary.Customer)
                    ? string.Format("CM 전역 검색 — 사용 중 {0}개 번호 ({1}건) 발견", dupNumberCount, dupObjectCount)
                    : string.Format("'{0}' — 사용 중 {1}개 번호 ({2}건) 발견", summary.Customer, dupNumberCount, dupObjectCount);
            }
            else if (hasMissing)
            {
                headline = string.IsNullOrWhiteSpace(summary.Customer)
                    ? "CM 대상 폴더 확인 필요"
                    : string.Format("'{0}' — CM 대상 폴더 확인 필요", summary.Customer);
            }
            else
            {
                headline = string.IsNullOrWhiteSpace(summary.Customer)
                    ? "CM 전역 검색 — 전체 미사용 (사용 가능)"
                    : string.Format("'{0}' — 전체 미사용 (사용 가능)", summary.Customer);
            }

            ShowTwoColumnCheckDialog(
                owner,
                winTitle,
                statusTone,
                headline,
                checkDn ? dnList.Count : 0,
                checkCti ? ctiList.Count : 0,
                dupNumberCount,
                "사용 중 발견 항목 및 위치",
                dupCards,
                checkDn,
                checkCti,
                cleanDns,
                cleanCtis,
                missingFolders.Count > 0 ? "CM 대상 폴더 확인 필요" : null,
                missingFolders,
                copyText.ToString());
        }

        public static void ShowPbxCheckResult(Window owner, PbxCheckSummary summary)
        {
            if (summary == null) return;

            List<PbxCheckItem> items = summary.Items ?? new List<PbxCheckItem>();
            List<string> dnList = summary.DnList ?? new List<string>();
            List<string> ctiList = summary.CtiList ?? new List<string>();
            bool checkDn = dnList.Count > 0;
            bool checkCti = ctiList.Count > 0;

            List<Border> dupCards = new List<Border>();
            List<string> cleanDns = new List<string>();
            List<string> cleanCtis = new List<string>();
            StringBuilder copyText = new StringBuilder();
            string centerHeader = string.IsNullOrWhiteSpace(summary.SelectedCustomer) ? "(미선택)" : summary.SelectedCustomer;
            copyText.AppendLine(string.Format("[PBX 내선/CTI ID 검색 결과] 기준 센터: {0}", centerHeader));

            for (int i = 0; i < items.Count; i++)
            {
                PbxCheckItem item = items[i];
                if (item == null || string.IsNullOrWhiteSpace(item.Number)) continue;

                bool isDn = string.Equals(item.Kind, "E", StringComparison.OrdinalIgnoreCase);
                if (!item.Exists)
                {
                    if (isDn) cleanDns.Add(item.Number);
                    else cleanCtis.Add(item.Number);
                    continue;
                }

                string kindLabel = isDn ? "내선" : "CTI";
                string centerDisp = string.IsNullOrWhiteSpace(item.CustomerName) ? "unknown" : item.CustomerName;
                string entDisp = string.IsNullOrWhiteSpace(item.EntityNo) ? "-" : item.EntityNo;
                List<string> pgList = item.Pgs ?? new List<string>();
                string pgTupleDisp = pgList.Count > 0 ? string.Join("/", pgList.ToArray()) : "-";
                string pgFullDisp = pgList.Count > 0 ? string.Join(", ", pgList.ToArray()) : "-";

                string tupleText = string.Format("({0}, {1}, {2})", centerDisp, entDisp, pgTupleDisp);
                copyText.AppendLine(string.Format("[사용 중] {0} {1} -> ({2}, Entity No: {3}, Agent PG: {4})", kindLabel, item.Number, centerDisp, entDisp, pgFullDisp));

                StackPanel cardBody = new StackPanel();
                Border rightBadge = string.Equals(centerDisp, "unknown", StringComparison.OrdinalIgnoreCase)
                    ? MakePillBadge("unknown", "WarnBgBrush", "WarnFgBrush", "WarnBorderBrush", false, false, new Thickness(8, 0, 0, 0))
                    : null;
                cardBody.Children.Add(MakeDupCardHeader(kindLabel, item.Number, rightBadge));

                Border hitRow = MakeErrHitRow(new Thickness(0));
                StackPanel hitStack = new StackPanel();

                TextBlock hitHeader = new TextBlock();
                hitHeader.Text = string.Format("PBX 사용 중 · {0}", tupleText);
                hitHeader.FontSize = 11.5;
                hitHeader.FontWeight = FontWeights.Bold;
                hitHeader.Foreground = ResBrush("ErrFgBrush", Brushes.LightCoral);
                hitHeader.TextWrapping = TextWrapping.Wrap;
                hitStack.Children.Add(hitHeader);

                TextBlock detailTxt = new TextBlock();
                detailTxt.Text = string.Format("센터: {0}  |  Entity No: {1}  |  Agent PG: {2}", centerDisp, entDisp, pgFullDisp);
                detailTxt.FontFamily = new FontFamily("Consolas");
                detailTxt.FontSize = 11.5;
                detailTxt.Foreground = ResBrush("TextBrush", Brushes.White);
                detailTxt.TextWrapping = TextWrapping.Wrap;
                detailTxt.Margin = new Thickness(0, 2, 0, 0);
                hitStack.Children.Add(detailTxt);

                hitRow.Child = hitStack;
                cardBody.Children.Add(hitRow);
                dupCards.Add(MakeDupCardContainer(cardBody));
            }

            if (cleanDns.Count > 0) copyText.AppendLine("[미사용 - 내선] " + string.Join(", ", cleanDns.ToArray()));
            if (cleanCtis.Count > 0) copyText.AppendLine("[미사용 - CTI] " + string.Join(", ", cleanCtis.ToArray()));

            bool hasDups = dupCards.Count > 0;
            string statusTone = hasDups ? "Err" : "Ok";
            string winTitle = hasDups ? "PBX 내선/CTI ID 검색 결과 — 사용 중 발견" : "PBX 내선/CTI ID 검색 결과 — 사용 가능";
            string headline;
            if (hasDups)
            {
                headline = string.IsNullOrWhiteSpace(summary.SelectedCustomer)
                    ? string.Format("PBX 사용 중 {0}개 번호 발견", dupCards.Count)
                    : string.Format("'{0}' — PBX 사용 중 {1}개 번호 발견", summary.SelectedCustomer, dupCards.Count);
            }
            else
            {
                headline = string.IsNullOrWhiteSpace(summary.SelectedCustomer)
                    ? "PBX 전체 미사용 (사용 가능)"
                    : string.Format("'{0}' — PBX 전체 미사용 (사용 가능)", summary.SelectedCustomer);
            }

            ShowTwoColumnCheckDialog(
                owner,
                winTitle,
                statusTone,
                headline,
                checkDn ? dnList.Count : 0,
                checkCti ? ctiList.Count : 0,
                dupCards.Count,
                "PBX 사용 중 항목 (센터, Entity No, Agent PG)",
                dupCards,
                checkDn,
                checkCti,
                cleanDns,
                cleanCtis,
                null,
                null,
                copyText.ToString());
        }

        private static Grid BuildAlertHeader(string tonePrefix, string iconChar, string headlineText, double headlineFontSize, string detailText, FrameworkElement headExtra)
        {
            Grid top = new Grid();
            ColumnDefinition c0 = new ColumnDefinition(); c0.Width = GridLength.Auto;
            ColumnDefinition c1 = new ColumnDefinition(); c1.Width = new GridLength(1, GridUnitType.Star);
            top.ColumnDefinitions.Add(c0);
            top.ColumnDefinitions.Add(c1);

            Border iconCircle = new Border();
            iconCircle.Width = 34; iconCircle.Height = 34; iconCircle.CornerRadius = new CornerRadius(17);
            iconCircle.Background = ResBrush(tonePrefix + "BgBrush", Brushes.DarkRed);
            iconCircle.BorderBrush = ResBrush(tonePrefix + "BorderBrush", Brushes.IndianRed);
            iconCircle.BorderThickness = new Thickness(1);
            iconCircle.VerticalAlignment = VerticalAlignment.Top;
            iconCircle.Margin = new Thickness(0, 0, 12, 0);

            TextBlock glyph = new TextBlock();
            glyph.Text = iconChar;
            glyph.FontSize = 16;
            glyph.FontWeight = FontWeights.Bold;
            glyph.Foreground = ResBrush(tonePrefix + "FgBrush", Brushes.White);
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            glyph.VerticalAlignment = VerticalAlignment.Center;
            iconCircle.Child = glyph;
            Grid.SetColumn(iconCircle, 0);
            top.Children.Add(iconCircle);

            StackPanel headRight = new StackPanel();
            TextBlock headlineTxt = new TextBlock();
            headlineTxt.Text = headlineText ?? "";
            headlineTxt.FontSize = headlineFontSize;
            headlineTxt.FontWeight = FontWeights.Bold;
            headlineTxt.Foreground = ResBrush(tonePrefix + "FgBrush", Brushes.White);
            headlineTxt.TextWrapping = TextWrapping.Wrap;
            headRight.Children.Add(headlineTxt);

            if (!string.IsNullOrEmpty(detailText))
            {
                TextBlock msgSub = new TextBlock();
                msgSub.Text = detailText;
                msgSub.FontSize = 12;
                msgSub.Foreground = ResBrush("MutedBrush", Brushes.LightGray);
                msgSub.TextWrapping = TextWrapping.Wrap;
                msgSub.Margin = new Thickness(0, 6, 0, 0);
                headRight.Children.Add(msgSub);
            }

            if (headExtra != null)
            {
                headRight.Children.Add(headExtra);
            }

            Grid.SetColumn(headRight, 1);
            top.Children.Add(headRight);
            return top;
        }

        public static void ShowPersonCheckResult(Window owner, string customerName, PersonCheckSummary summary, List<string> cleanUserNames)
        {
            if (summary == null) return;

            List<PersonCheckMatchDto> empMatches = summary.empMatches ?? new List<PersonCheckMatchDto>();
            List<PersonCheckMatchDto> userMatches = summary.userMatches ?? new List<PersonCheckMatchDto>();
            List<string> cleanEmps = summary.cleanEmpIds ?? new List<string>();
            List<string> cleanUsers = cleanUserNames ?? new List<string>();

            List<Border> dupCards = new List<Border>();
            StringBuilder copyText = new StringBuilder();
            copyText.AppendLine("[Person Employee ID / User Name 중복 체크 결과]");

            Action<string, List<PersonCheckMatchDto>> appendCards = delegate(string kindLabel, List<PersonCheckMatchDto> list)
            {
                if (list.Count == 0) return;
                Dictionary<string, List<PersonCheckMatchDto>> grouped = new Dictionary<string, List<PersonCheckMatchDto>>(StringComparer.OrdinalIgnoreCase);
                List<string> keys = new List<string>();
                for (int i = 0; i < list.Count; i++)
                {
                    string k = list[i].inputId ?? "";
                    if (!grouped.ContainsKey(k))
                    {
                        grouped[k] = new List<PersonCheckMatchDto>();
                        keys.Add(k);
                    }
                    grouped[k].Add(list[i]);
                }

                for (int k = 0; k < keys.Count; k++)
                {
                    string key = keys[k];
                    List<PersonCheckMatchDto> hits = grouped[key];
                    StackPanel cardBody = new StackPanel();
                    DockPanel cardHead = new DockPanel();
                    cardHead.LastChildFill = false;

                    TextBlock numLbl = new TextBlock();
                    numLbl.Text = string.Format("{0}  {1}", kindLabel, key);
                    numLbl.FontSize = 13.5;
                    numLbl.FontWeight = FontWeights.Bold;
                    numLbl.Foreground = ResBrush("ErrFgBrush", Brushes.LightCoral);
                    DockPanel.SetDock(numLbl, Dock.Left);
                    cardHead.Children.Add(numLbl);

                    Border dupTag = MakePillBadge(string.Format("중복 {0}건", hits.Count), "ErrBgBrush", "ErrFgBrush", "ErrBorderBrush", true, true, new Thickness(0));
                    DockPanel.SetDock(dupTag, Dock.Right);
                    cardHead.Children.Add(dupTag);
                    cardBody.Children.Add(cardHead);

                    copyText.AppendLine(string.Format("[중복 {0}] {1} ({2}건)", kindLabel, key, hits.Count));
                    for (int i = 0; i < hits.Count; i++)
                    {
                        PersonCheckMatchDto m = hits[i];
                        Border hitRow = new Border();
                        hitRow.Background = ResBrush("BgBrush", Brushes.Black);
                        hitRow.BorderBrush = ResBrush("BorderBrush", Brushes.Gray);
                        hitRow.BorderThickness = new Thickness(1);
                        hitRow.CornerRadius = new CornerRadius(5);
                        hitRow.Padding = new Thickness(9, 6, 9, 6);
                        hitRow.Margin = new Thickness(0, 6, 0, 0);

                        StackPanel hitStack = new StackPanel();
                        TextBlock objInfo = new TextBlock();
                        objInfo.Text = string.Format("User: {0}  |  EmpID: {1}", m.userName ?? "", m.employeeId ?? "");
                        objInfo.FontSize = 11.5;
                        objInfo.FontWeight = FontWeights.SemiBold;
                        objInfo.Foreground = ResBrush("PrimaryHoverBrush", Brushes.DeepSkyBlue);
                        hitStack.Children.Add(objInfo);

                        TextBlock locTxt = new TextBlock();
                        locTxt.Text = "위치: " + (string.IsNullOrWhiteSpace(m.folderPath) ? "-" : m.folderPath);
                        locTxt.FontFamily = new FontFamily("Consolas");
                        locTxt.FontSize = 11.5;
                        locTxt.Foreground = ResBrush("TextBrush", Brushes.White);
                        locTxt.TextWrapping = TextWrapping.Wrap;
                        locTxt.Margin = new Thickness(0, 2, 0, 0);
                        hitStack.Children.Add(locTxt);

                        hitRow.Child = hitStack;
                        cardBody.Children.Add(hitRow);
                        copyText.AppendLine(string.Format("  - User={0}, EmpID={1} -> {2}", m.userName ?? "", m.employeeId ?? "", m.folderPath ?? ""));
                    }
                    dupCards.Add(MakeDupCardContainer(cardBody));
                }
            };

            appendCards("Employee ID", empMatches);
            appendCards("User Name", userMatches);

            if (cleanEmps.Count > 0) copyText.AppendLine("[사용 가능 Employee ID] " + string.Join(", ", cleanEmps.ToArray()));
            if (cleanUsers.Count > 0) copyText.AppendLine("[사용 가능 User Name] " + string.Join(", ", cleanUsers.ToArray()));

            int empDupCnt = summary.dupEmpIds != null ? summary.dupEmpIds.Count : 0;
            int userDupCnt = summary.dupUserNames != null ? summary.dupUserNames.Count : 0;
            int totalDups = empDupCnt + userDupCnt;
            bool hasDups = dupCards.Count > 0;
            string statusTone = hasDups ? "Err" : "Ok";
            string winTitle = hasDups ? "CM Person 중복체크 결과 — 중복 발견" : "CM Person 중복체크 결과 — 중복 없음";
            string prefix = string.IsNullOrWhiteSpace(customerName) ? "CM 전역 검색" : string.Format("'{0}'", customerName);
            string headline = hasDups
                ? string.Format("{0} — 중복 {1}건 발견 (EmpID {2} · UserName {3})", prefix, totalDups, empDupCnt, userDupCnt)
                : string.Format("{0} — 중복 없음 (대상 {1}명 전체 사용 가능)", prefix, summary.totalChecked);

            ShowTwoColumnCheckDialogEx(
                owner,
                winTitle,
                statusTone,
                headline,
                string.Format("검사 대상: {0}명 (CM 전체 {1}명 조회)", summary.totalChecked, summary.globalPersonCount),
                totalDups,
                string.Format("사용 가능: EmpID {0} · User {1}", cleanEmps.Count, cleanUsers.Count),
                "중복 발견 항목 및 위치",
                dupCards,
                true,
                cleanUsers.Count > 0 || userDupCnt > 0,
                cleanEmps,
                cleanUsers,
                "사용 가능 Employee ID",
                "사용 가능 User Name",
                null,
                null,
                copyText.ToString());
        }

        private static void ShowTwoColumnCheckDialog(
            Window owner,
            string windowTitle,
            string statusTone,
            string headline,
            int checkedDnCount,
            int checkedCtiCount,
            int dupCount,
            string leftColumnTitlePrefix,
            List<Border> dupCards,
            bool checkDn,
            bool checkCti,
            List<string> cleanDns,
            List<string> cleanCtis,
            string warnBoxTitle,
            List<string> warnItems,
            string copyPayload)
        {
            ShowTwoColumnCheckDialogEx(
                owner,
                windowTitle,
                statusTone,
                headline,
                string.Format("검색 대상: 내선 {0} · CTI {1}", checkedDnCount, checkedCtiCount),
                dupCount,
                string.Format("사용 가능 (미사용): 내선 {0} · CTI {1}", cleanDns.Count, cleanCtis.Count),
                leftColumnTitlePrefix,
                dupCards,
                checkDn,
                checkCti,
                cleanDns,
                cleanCtis,
                "내선",
                "CTI",
                warnBoxTitle,
                warnItems,
                copyPayload,
                true);
        }

        private static void ShowTwoColumnCheckDialogEx(
            Window owner,
            string windowTitle,
            string statusTone,
            string headline,
            string checkedBadgeText,
            int dupCount,
            string cleanBadgeText,
            string leftColumnTitlePrefix,
            List<Border> dupCards,
            bool checkGroup1,
            bool checkGroup2,
            List<string> cleanGroup1,
            List<string> cleanGroup2,
            string group1Label,
            string group2Label,
            string warnBoxTitle,
            List<string> warnItems,
            string copyPayload,
            bool isSearchMode = false)
        {
            StackPanel shell;
            Window dlg = CreateThemedDialogShell(owner, windowTitle, 780, out shell);

            StackPanel root = new StackPanel();
            root.Margin = new Thickness(18, 16, 18, 16);

            bool isOk = string.Equals(statusTone, "Ok", StringComparison.Ordinal);
            string tonePrefix = isOk ? "Ok" : (string.Equals(statusTone, "Warn", StringComparison.Ordinal) ? "Warn" : "Err");
            string iconChar = isOk ? "✓" : "!";

            WrapPanel statsRow = new WrapPanel();
            statsRow.Margin = new Thickness(0, 8, 0, 0);
            statsRow.Children.Add(MakePillBadge(
                checkedBadgeText,
                "ChipBgBrush", "ChipFgBrush", null, false, false, new Thickness(0, 2, 6, 2)));
            statsRow.Children.Add(MakePillBadge(
                string.Format(isSearchMode ? "사용 중: {0}건" : "중복 발견: {0}건", dupCount),
                dupCount > 0 ? "ErrBgBrush" : "ChipBgBrush",
                dupCount > 0 ? "ErrFgBrush" : "MutedBrush",
                dupCount > 0 ? "ErrBorderBrush" : "BorderBrush",
                dupCount > 0,
                false,
                new Thickness(0, 2, 6, 2)));
            statsRow.Children.Add(MakePillBadge(
                cleanBadgeText,
                "OkBgBrush", "OkFgBrush", "OkBorderBrush", true, false, new Thickness(0, 2, 0, 2)));

            root.Children.Add(BuildAlertHeader(tonePrefix, iconChar, headline, 14, null, statsRow));

            if (!string.IsNullOrEmpty(warnBoxTitle) && warnItems != null && warnItems.Count > 0)
            {
                Border warnBox = new Border();
                warnBox.Background = ResBrush("WarnBgBrush", Brushes.DarkGoldenrod);
                warnBox.BorderBrush = ResBrush("WarnBorderBrush", Brushes.Gold);
                warnBox.BorderThickness = new Thickness(1);
                warnBox.CornerRadius = new CornerRadius(6);
                warnBox.Padding = new Thickness(11, 9, 11, 9);
                warnBox.Margin = new Thickness(0, 12, 0, 0);

                StackPanel ws = new StackPanel();
                TextBlock wt = new TextBlock();
                wt.Text = warnBoxTitle;
                wt.FontSize = 12; wt.FontWeight = FontWeights.Bold;
                wt.Foreground = ResBrush("WarnFgBrush", Brushes.Gold);
                ws.Children.Add(wt);
                for (int i = 0; i < warnItems.Count; i++)
                {
                    TextBlock wm = new TextBlock();
                    wm.Text = "• " + warnItems[i];
                    wm.FontSize = 11.5;
                    wm.Foreground = ResBrush("TextBrush", Brushes.White);
                    wm.TextWrapping = TextWrapping.Wrap;
                    wm.Margin = new Thickness(0, 3, 0, 0);
                    ws.Children.Add(wm);
                }
                warnBox.Child = ws;
                root.Children.Add(warnBox);
            }

            // Two-column layout: Left = Duplicates, Right = Clean Available Numbers
            Grid bodyCols = new Grid();
            bodyCols.Margin = new Thickness(0, 14, 0, 0);
            ColumnDefinition leftColDef = new ColumnDefinition(); leftColDef.Width = new GridLength(1.5, GridUnitType.Star);
            ColumnDefinition midGapDef = new ColumnDefinition(); midGapDef.Width = new GridLength(12);
            ColumnDefinition rightColDef = new ColumnDefinition(); rightColDef.Width = new GridLength(0.95, GridUnitType.Star);
            bodyCols.ColumnDefinitions.Add(leftColDef);
            bodyCols.ColumnDefinitions.Add(midGapDef);
            bodyCols.ColumnDefinitions.Add(rightColDef);

            // Left Column: Duplicates
            DockPanel leftPanel = new DockPanel();
            TextBlock dupSectionTitle = new TextBlock();
            dupSectionTitle.Text = string.Format("{0} ({1}건)", leftColumnTitlePrefix, dupCards.Count);
            dupSectionTitle.FontSize = 12.5;
            dupSectionTitle.FontWeight = FontWeights.Bold;
            dupSectionTitle.Foreground = dupCards.Count > 0 ? ResBrush("ErrFgBrush", Brushes.LightCoral) : ResBrush("MutedBrush", Brushes.Gray);
            dupSectionTitle.Margin = new Thickness(0, 0, 0, 7);
            DockPanel.SetDock(dupSectionTitle, Dock.Top);
            leftPanel.Children.Add(dupSectionTitle);

            ScrollViewer dupScroller = new ScrollViewer();
            dupScroller.MaxHeight = 410;
            dupScroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            StackPanel dupStack = new StackPanel();
            if (dupCards.Count == 0)
            {
                Border emptyDup = new Border();
                emptyDup.Background = ResBrush("SurfaceAltBrush", Brushes.Black);
                emptyDup.BorderBrush = ResBrush("BorderBrush", Brushes.Gray);
                emptyDup.BorderThickness = new Thickness(1);
                emptyDup.CornerRadius = new CornerRadius(6);
                emptyDup.Padding = new Thickness(11, 9, 11, 9);
                TextBlock noDupTxt = new TextBlock();
                noDupTxt.Text = isSearchMode ? "사용 중인 번호가 없습니다." : "중복된 번호가 없습니다.";
                noDupTxt.FontSize = 12;
                noDupTxt.Foreground = ResBrush("MutedBrush", Brushes.Gray);
                emptyDup.Child = noDupTxt;
                dupStack.Children.Add(emptyDup);
            }
            else
            {
                for (int i = 0; i < dupCards.Count; i++)
                {
                    dupStack.Children.Add(dupCards[i]);
                }
            }
            dupScroller.Content = dupStack;
            leftPanel.Children.Add(dupScroller);
            Grid.SetColumn(leftPanel, 0);
            bodyCols.Children.Add(leftPanel);

            // Right Column: Clean Available Numbers
            DockPanel rightPanel = new DockPanel();
            TextBlock cleanTitle = new TextBlock();
            cleanTitle.Text = string.Format(
                isSearchMode ? "미사용 · 사용 가능 ({0}건)" : "중복 없음 · 사용 가능 ({0}건)",
                cleanGroup1.Count + cleanGroup2.Count);
            cleanTitle.FontSize = 12.5;
            cleanTitle.FontWeight = FontWeights.Bold;
            cleanTitle.Foreground = ResBrush("OkFgBrush", Brushes.LightGreen);
            cleanTitle.Margin = new Thickness(0, 0, 0, 7);
            DockPanel.SetDock(cleanTitle, Dock.Top);
            rightPanel.Children.Add(cleanTitle);

            Border cleanCard = new Border();
            cleanCard.Background = ResBrush("SurfaceAltBrush", Brushes.Black);
            cleanCard.BorderBrush = ResBrush("OkBorderBrush", Brushes.SeaGreen);
            cleanCard.BorderThickness = new Thickness(1);
            cleanCard.CornerRadius = new CornerRadius(6);
            cleanCard.Padding = new Thickness(11, 10, 11, 10);
            cleanCard.VerticalAlignment = VerticalAlignment.Top;

            ScrollViewer cleanScroller = new ScrollViewer();
            cleanScroller.MaxHeight = 390;
            cleanScroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            StackPanel cleanBody = new StackPanel();
            if (cleanGroup1.Count == 0 && cleanGroup2.Count == 0)
            {
                TextBlock noClean = new TextBlock();
                noClean.Text = isSearchMode ? "사용 가능한 항목이 없습니다." : "중복 없는 항목이 없습니다.";
                noClean.FontSize = 12;
                noClean.TextWrapping = TextWrapping.Wrap;
                noClean.Foreground = ResBrush("MutedBrush", Brushes.Gray);
                cleanBody.Children.Add(noClean);
            }
            else
            {
                if (checkGroup1)
                {
                    AddCleanNumberGroup(cleanBody, group1Label, cleanGroup1, new Thickness(0, 0, 0, checkGroup2 ? 8 : 0));
                }
                if (checkGroup2)
                {
                    AddCleanNumberGroup(cleanBody, group2Label, cleanGroup2, new Thickness(0));
                }
            }
            cleanScroller.Content = cleanBody;
            cleanCard.Child = cleanScroller;
            rightPanel.Children.Add(cleanCard);
            Grid.SetColumn(rightPanel, 2);
            bodyCols.Children.Add(rightPanel);

            root.Children.Add(bodyCols);

            DockPanel btnBar = new DockPanel();
            btnBar.LastChildFill = false;
            btnBar.Margin = new Thickness(0, 14, 0, 0);

            Button btnCopy = new Button();
            btnCopy.Content = "결과 복사";
            btnCopy.Style = Application.Current.Resources["BaseBtn"] as Style;
            btnCopy.MinWidth = 86;
            btnCopy.Click += delegate
            {
                try
                {
                    Clipboard.SetText(copyPayload ?? "");
                    btnCopy.Content = "복사됨 ✓";
                }
                catch { }
            };
            DockPanel.SetDock(btnCopy, Dock.Left);
            btnBar.Children.Add(btnCopy);

            Button btnOk = new Button();
            btnOk.Content = "확인";
            btnOk.Style = Application.Current.Resources["PrimaryBtn"] as Style;
            btnOk.MinWidth = 86;
            btnOk.IsDefault = true;
            btnOk.Click += delegate { dlg.DialogResult = true; dlg.Close(); };
            DockPanel.SetDock(btnOk, Dock.Right);
            btnBar.Children.Add(btnOk);

            root.Children.Add(btnBar);
            shell.Children.Add(root);
            ShowThemedModal(owner, dlg, btnOk, true, true);
        }

        private static bool ShowThemedConfirm(Window owner, string windowTitle, string headline, string detail, bool isDanger, string confirmText)
        {
            StackPanel shell;
            Window dlg = CreateThemedDialogShell(owner, windowTitle, 440, out shell);

            StackPanel root = new StackPanel();
            root.Margin = new Thickness(18);
            root.Children.Add(BuildAlertHeader(isDanger ? "Err" : "Warn", "!", headline, 13, detail, null));

            StackPanel btnRow = new StackPanel();
            btnRow.Orientation = Orientation.Horizontal;
            btnRow.HorizontalAlignment = HorizontalAlignment.Right;
            btnRow.Margin = new Thickness(0, 16, 0, 0);

            Button btnCancel = new Button();
            btnCancel.Content = "취소";
            btnCancel.Style = Application.Current.Resources["BaseBtn"] as Style;
            btnCancel.MinWidth = 72;
            btnCancel.Margin = new Thickness(0, 0, 8, 0);
            btnCancel.Click += delegate { dlg.DialogResult = false; dlg.Close(); };
            btnRow.Children.Add(btnCancel);

            Button btnOk = new Button();
            btnOk.Content = confirmText;
            btnOk.Style = Application.Current.Resources[isDanger ? "DangerBtn" : "PrimaryBtn"] as Style;
            btnOk.MinWidth = 86;
            btnOk.IsDefault = !isDanger;
            btnOk.Click += delegate { dlg.DialogResult = true; dlg.Close(); };
            btnRow.Children.Add(btnOk);

            root.Children.Add(btnRow);
            shell.Children.Add(root);

            return ShowThemedModal(owner, dlg, isDanger ? btnCancel : btnOk, true, false);
        }

        private static bool ShowDeleteTypedConfirm(Window owner, string windowTitle, string headline, string targetDetail, string customWarningDetail)
        {
            const string requiredWord = "삭제한다";
            StackPanel shell;
            Window dlg = CreateThemedDialogShell(owner, string.IsNullOrWhiteSpace(windowTitle) ? "삭제 확인" : windowTitle, 480, out shell);

            StackPanel root = new StackPanel();
            root.Margin = new Thickness(18);

            string warningDetail = !string.IsNullOrWhiteSpace(customWarningDetail)
                ? customWarningDetail
                : ("의도하지 않은 내선번호/CTI ID 삭제 시 센터 운영에 문제가 생길 수 있습니다.\n" +
                   "다시 한 번 삭제하려는 내선번호/CTI ID 가 정확한지 확인바랍니다.\n\n" +
                   "정말 삭제하시려면 '삭제한다' 를 입력해 주십시오.");
            root.Children.Add(BuildAlertHeader("Err", "!", string.IsNullOrWhiteSpace(headline) ? "삭제 확인" : headline, 13.5, warningDetail, null));

            if (!string.IsNullOrWhiteSpace(targetDetail))
            {
                Border detailBox = new Border();
                detailBox.Margin = new Thickness(46, 10, 0, 0);
                detailBox.Padding = new Thickness(10, 7, 10, 7);
                detailBox.CornerRadius = new CornerRadius(6);
                detailBox.Background = ResBrush("SurfaceAltBrush", Brushes.Black);
                detailBox.BorderBrush = ResBrush("BorderBrush", Brushes.Gray);
                detailBox.BorderThickness = new Thickness(1);

                TextBlock dt = new TextBlock();
                dt.Text = targetDetail.Trim();
                dt.FontSize = 11.5;
                dt.Foreground = ResBrush("TextBrush", Brushes.White);
                dt.TextWrapping = TextWrapping.Wrap;
                detailBox.Child = dt;
                root.Children.Add(detailBox);
            }

            Grid inputWrap = new Grid();
            inputWrap.Margin = new Thickness(46, 12, 0, 0);

            TextBox txtConfirm = new TextBox();
            Style tbStyle = Application.Current.Resources["ModernTextBox"] as Style;
            if (tbStyle != null) txtConfirm.Style = tbStyle;
            txtConfirm.Height = 34;
            txtConfirm.FontSize = 13;
            txtConfirm.FontWeight = FontWeights.SemiBold;
            txtConfirm.Padding = new Thickness(10, 2, 10, 2);

            TextBlock hint = new TextBlock();
            hint.Text = "삭제한다";
            hint.FontSize = 12.5;
            hint.Foreground = ResBrush("MutedBrush", Brushes.Gray);
            hint.Opacity = 0.55;
            hint.VerticalAlignment = VerticalAlignment.Center;
            hint.Margin = new Thickness(11, 0, 0, 0);
            hint.IsHitTestVisible = false;

            inputWrap.Children.Add(txtConfirm);
            inputWrap.Children.Add(hint);
            root.Children.Add(inputWrap);

            StackPanel btnRow = new StackPanel();
            btnRow.Orientation = Orientation.Horizontal;
            btnRow.HorizontalAlignment = HorizontalAlignment.Right;
            btnRow.Margin = new Thickness(0, 16, 0, 0);

            Button btnCancel = new Button();
            btnCancel.Content = "취소";
            btnCancel.Style = Application.Current.Resources["BaseBtn"] as Style;
            btnCancel.MinWidth = 72;
            btnCancel.Margin = new Thickness(0, 0, 8, 0);
            btnCancel.Click += delegate { dlg.DialogResult = false; dlg.Close(); };
            btnRow.Children.Add(btnCancel);

            Button btnProceed = new Button();
            btnProceed.Content = "삭제 진행";
            btnProceed.Style = Application.Current.Resources["DangerBtn"] as Style;
            btnProceed.MinWidth = 90;
            btnProceed.IsEnabled = false;
            btnProceed.Click += delegate
            {
                if (string.Equals((txtConfirm.Text ?? "").Trim(), requiredWord, StringComparison.Ordinal))
                {
                    dlg.DialogResult = true;
                    dlg.Close();
                }
            };
            btnRow.Children.Add(btnProceed);

            txtConfirm.TextChanged += delegate
            {
                string val = txtConfirm.Text ?? "";
                hint.Visibility = val.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                btnProceed.IsEnabled = string.Equals(val.Trim(), requiredWord, StringComparison.Ordinal);
            };
            txtConfirm.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                Key eff = (e.Key == Key.ImeProcessed) ? e.ImeProcessedKey : e.Key;
                if (eff == Key.Enter)
                {
                    if (string.Equals((txtConfirm.Text ?? "").Trim(), requiredWord, StringComparison.Ordinal))
                    {
                        e.Handled = true;
                        CloseDialogDeferred(dlg, true);
                    }
                    else if (e.Key == Key.Enter)
                    {
                        e.Handled = true;
                    }
                }
            };

            root.Children.Add(btnRow);
            shell.Children.Add(root);

            return ShowThemedModal(owner, dlg, txtConfirm, true, false);
        }

        public static MessageBoxResult PromptSaveDiscardCancel(
            Window owner,
            string title,
            string headline,
            string detail)
        {
            StackPanel shell;
            Window dlg = CreateThemedDialogShell(owner, string.IsNullOrWhiteSpace(title) ? "미저장 변경사항" : title, 470, out shell);

            StackPanel root = new StackPanel();
            root.Margin = new Thickness(18);
            root.Children.Add(BuildAlertHeader("Warn", "!", string.IsNullOrWhiteSpace(headline) ? "저장되지 않은 변경사항이 있습니다." : headline, 13.5, detail, null));

            MessageBoxResult chosen = MessageBoxResult.Cancel;

            StackPanel btnRow = new StackPanel();
            btnRow.Orientation = Orientation.Horizontal;
            btnRow.HorizontalAlignment = HorizontalAlignment.Right;
            btnRow.Margin = new Thickness(0, 18, 0, 0);

            Button btnCancel = new Button();
            btnCancel.Content = "취소";
            btnCancel.Style = Application.Current.Resources["BaseBtn"] as Style;
            btnCancel.MinWidth = 74;
            btnCancel.Margin = new Thickness(0, 0, 8, 0);
            btnCancel.IsCancel = true;
            btnCancel.Click += delegate { chosen = MessageBoxResult.Cancel; dlg.DialogResult = false; dlg.Close(); };
            btnRow.Children.Add(btnCancel);

            Button btnDiscard = new Button();
            btnDiscard.Content = "변경 버리고 원복";
            btnDiscard.Style = Application.Current.Resources["WarnBtn"] as Style;
            btnDiscard.MinWidth = 118;
            btnDiscard.Margin = new Thickness(0, 0, 8, 0);
            btnDiscard.Click += delegate { chosen = MessageBoxResult.No; dlg.DialogResult = true; dlg.Close(); };
            btnRow.Children.Add(btnDiscard);

            Button btnSave = new Button();
            btnSave.Content = "저장 후 진행";
            btnSave.Style = Application.Current.Resources["PrimaryBtn"] as Style;
            btnSave.MinWidth = 104;
            btnSave.IsDefault = true;
            btnSave.Click += delegate { chosen = MessageBoxResult.Yes; dlg.DialogResult = true; dlg.Close(); };
            btnRow.Children.Add(btnSave);

            root.Children.Add(btnRow);
            shell.Children.Add(root);

            ShowThemedModal(owner, dlg, btnSave, true, false);
            return chosen;
        }

        public static DialPlanBackupEntry PickDialPlanBackup(Window owner, IList<DialPlanBackupEntry> backups)
        {
            if (backups == null || backups.Count == 0)
            {
                ShowSyntaxToast(owner, false, "백업 없음", "저장 시 자동 생성된 백업 파일(backups/)이 아직 없습니다.");
                return null;
            }

            StackPanel shell;
            Window dlg = CreateThemedDialogShell(owner, "자동 백업 시점 복원", 540, out shell);

            StackPanel root = new StackPanel();
            root.Margin = new Thickness(18);
            root.Children.Add(BuildAlertHeader(
                "Ok",
                "↺",
                "복원할 자동 백업 시점을 선택하세요",
                13.5,
                "선번장을 저장할 때마다 직전 파일이 backups 폴더에 자동 보관됩니다(최대 20건).",
                null));

            ListBox lb = new ListBox();
            lb.Height = 230;
            lb.Margin = new Thickness(0, 12, 0, 0);
            lb.Background = ResBrush("InputBgBrush", Brushes.White);
            lb.Foreground = ResBrush("TextBrush", Brushes.Black);
            lb.BorderBrush = ResBrush("BorderBrush", Brushes.Gray);
            lb.BorderThickness = new Thickness(1);
            lb.FontSize = 12;
            lb.DisplayMemberPath = "DisplayLabel";
            lb.ItemsSource = backups;
            lb.SelectedIndex = 0;
            root.Children.Add(lb);

            DialPlanBackupEntry selected = null;

            StackPanel btnRow = new StackPanel();
            btnRow.Orientation = Orientation.Horizontal;
            btnRow.HorizontalAlignment = HorizontalAlignment.Right;
            btnRow.Margin = new Thickness(0, 14, 0, 0);

            Button btnCancel = new Button();
            btnCancel.Content = "취소";
            btnCancel.Style = Application.Current.Resources["BaseBtn"] as Style;
            btnCancel.MinWidth = 76;
            btnCancel.Margin = new Thickness(0, 0, 8, 0);
            btnCancel.IsCancel = true;
            btnCancel.Click += delegate { dlg.DialogResult = false; dlg.Close(); };
            btnRow.Children.Add(btnCancel);

            Button btnRestore = new Button();
            btnRestore.Content = "선택 백업으로 복원";
            btnRestore.Style = Application.Current.Resources["WarnBtn"] as Style;
            btnRestore.MinWidth = 135;
            btnRestore.IsDefault = true;
            btnRestore.Click += delegate
            {
                selected = lb.SelectedItem as DialPlanBackupEntry;
                if (selected != null)
                {
                    dlg.DialogResult = true;
                    dlg.Close();
                }
            };
            btnRow.Children.Add(btnRestore);

            lb.MouseDoubleClick += delegate
            {
                if (lb.SelectedItem is DialPlanBackupEntry)
                {
                    selected = lb.SelectedItem as DialPlanBackupEntry;
                    dlg.DialogResult = true;
                    dlg.Close();
                }
            };

            root.Children.Add(btnRow);
            shell.Children.Add(root);

            bool ok = ShowThemedModal(owner, dlg, lb, true, false);
            return ok ? selected : null;
        }
    }
}
