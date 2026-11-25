using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Input;
using CmOxeProvisioner.Services;

namespace CmOxeProvisioner.Views
{
    public partial class EmptyNumberFinderWindow : Window
    {
        private readonly EmptyNumberReport _report;

        public EmptyNumberFinderWindow(EmptyNumberReport report)
        {
            InitializeComponent();
            _report = report ?? new EmptyNumberReport();
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
            BindReport();
        }

        private void BindReport()
        {
            TxtEntityCount.Text = _report.FreeEntityNumbers.Count + "개 가용";
            TxtFreeEntities.Text = _report.FreeEntityNumbers.Count > 0
                ? string.Join(", ", _report.FreeEntityNumbers)
                : "(10~150 범위 내 미사용 Entity 없음)";

            TxtPrefixCount.Text = _report.FreeCenter6xxPrefixes.Count + "개 대역 가용";
            TxtFreePrefixes.Text = _report.FreeCenter6xxPrefixes.Count > 0
                ? string.Join(", ", _report.FreeCenter6xxPrefixes)
                : "(600~699 범위 내 가용 대역 없음)";

            TxtIvrCount.Text = _report.FreeIvrDns.Count + "개 구간";
            TxtFreeIvr.Text = _report.FreeIvrDns.Count > 0
                ? string.Join(", ", _report.FreeIvrDns)
                : "(할당 가능한 빈 IVR DN 구간 없음)";

            var sb = new StringBuilder();
            foreach (var kv in _report.FreeDvgByBand)
            {
                string line = kv.Value != null && kv.Value.Count > 0
                    ? string.Join(", ", kv.Value)
                    : "여유 번호 없음 (전체 할당됨)";
                sb.AppendLine("[" + kv.Key + "] (" + (kv.Value != null ? kv.Value.Count : 0) + "건): " + line);
            }
            TxtFreeDvg.Text = sb.ToString().TrimEnd();
        }

        private void BtnCopyAll_Click(object sender, RoutedEventArgs e)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== 미사용 Entity NO (10~150) ===");
            sb.AppendLine(TxtFreeEntities.Text);
            sb.AppendLine();
            sb.AppendLine("=== 미사용 6XX 센터 대역 ===");
            sb.AppendLine(TxtFreePrefixes.Text);
            sb.AppendLine();
            sb.AppendLine("=== 미사용 IP IVR DN ===");
            sb.AppendLine(TxtFreeIvr.Text);
            sb.AppendLine();
            sb.AppendLine("=== 미사용 D.V.G 멘트 ===");
            sb.AppendLine(TxtFreeDvg.Text);

            try
            {
                Clipboard.SetText(sb.ToString());
                PopupDialogs.ShowSyntaxToast(this, true, "복사 완료", "빈 번호 리포트가 클립보드에 복사되었습니다.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("클립보드 복사 실패: " + ex.Message, "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
