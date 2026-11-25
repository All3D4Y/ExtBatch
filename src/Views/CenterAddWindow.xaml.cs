using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CmOxeProvisioner.Models;
using CmOxeProvisioner.Services;

namespace CmOxeProvisioner.Views
{
    public partial class CenterAddWindow : Window
    {
        public bool HasCreatedCenter { get; private set; }
        public string LastCreatedCenterName { get; private set; }
        public CenterAddTabView InnerView { get { return CenterAddControl; } }

        public CenterAddWindow()
        {
            InitializeComponent();
            PreviewKeyDown += CenterAddWindow_PreviewKeyDown;
        }

        public void InitializeHandlers(
            List<CustomerInfo> customers,
            Func<CenterCreateInput, Task<CmCenterCheckResponse>> precheckHandler,
            Func<CenterCreateInput, Task<CenterSaveOutcome>> createHandler,
            Action<string, string> saveLogHandler)
        {
            CenterAddControl.SetCustomers(customers);
            CenterAddControl.PrecheckCenterAsyncHandler = precheckHandler;
            CenterAddControl.SaveLogHandler = saveLogHandler;

            CenterAddControl.CreateCenterAsyncHandler = async delegate(CenterCreateInput input)
            {
                if (createHandler == null)
                    return new CenterSaveOutcome { SavedToJson = false };

                var outcome = await createHandler(input);
                if (outcome != null && (outcome.SavedToJson || (outcome.CmResponse != null && outcome.CmResponse.failedCount == 0 && (outcome.CmResponse.createdCount > 0 || outcome.CmResponse.skippedCount > 0))))
                {
                    HasCreatedCenter = true;
                    LastCreatedCenterName = input != null ? input.CenterName : string.Empty;
                    TxtFooterHint.Text = "[완료] '" + LastCreatedCenterName + "' 등록이 반영되었습니다. 닫기를 누르면 목록이 갱신됩니다.";
                    var okBrush = TryFindResource("OkFgBrush") as Brush;
                    if (okBrush != null) TxtFooterHint.Foreground = okBrush;
                }
                return outcome;
            };
        }

        private void CenterAddWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CloseWithResult();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            CloseWithResult();
        }

        private void CloseWithResult()
        {
            try
            {
                DialogResult = HasCreatedCenter;
            }
            catch
            {
                // Ignore if window wasn't shown via ShowDialog
            }
            Close();
        }
    }
}
