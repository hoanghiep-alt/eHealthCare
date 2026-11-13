
using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Web.Mvc;

namespace eHealthCare.Controllers
{
    [Authorize]
    public class BaoHiemController : Controller
    {
        // Hiển thị trang nhập thẻ BHYT
        [HttpGet]
        public ActionResult NhapThe()
        {
            ViewBag.ThongBao = TempData["ThongBaoBHYT"];
            return View();
        }

        // Xử lý thông tin bệnh nhân nhập
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult NhapThe(
            string SoThe,
            string HoTen,
            string NgaySinh)
        {
            SoThe = (SoThe ?? "").Trim().ToUpperInvariant();
            HoTen = (HoTen ?? "").Trim();
            NgaySinh = (NgaySinh ?? "").Trim();

            // Kiểm tra dữ liệu
            if (string.IsNullOrWhiteSpace(SoThe) ||
                string.IsNullOrWhiteSpace(HoTen) ||
                string.IsNullOrWhiteSpace(NgaySinh))
            {
                ViewBag.Error = "Vui lòng nhập đầy đủ thông tin.";
                return View();
            }

            if (SoThe.Length > 30 ||
                !Regex.IsMatch(SoThe, @"^[A-Z0-9]+$"))
            {
                ViewBag.Error =
                    "Mã BHYT chỉ được chứa chữ và số, tối đa 30 ký tự.";
                return View();
            }

            if (HoTen.Length > 100)
            {
                ViewBag.Error = "Họ tên quá dài.";
                return View();
            }

            DateTime ngaySinh;

            bool hopLe = DateTime.TryParseExact(
                NgaySinh,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out ngaySinh
            );

            if (!hopLe ||
                ngaySinh.Date > DateTime.Today)
            {
                ViewBag.Error = "Ngày sinh không hợp lệ.";
                return View();
            }

            // Lưu tạm thông tin trong phiên đăng nhập
            Session["BHYT_SoThe"] = SoThe;
            Session["BHYT_HoTen"] = HoTen;
            Session["BHYT_NgaySinh"] = ngaySinh;
            Session["BHYT_DaNhap"] = true;

            TempData["ThongBaoBHYT"] =
                "Đã tiếp nhận thông tin BHYT. " +
                "Thông tin chưa được xác minh hiệu lực bảo hiểm.";

            return RedirectToAction("ChonKhoa", "KhamBenh");
        }
    }
}
