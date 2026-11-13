
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Configuration;
using System.Data.SqlClient;

namespace eHealthCare.Controllers
{
    public class HomeController : Controller
    {
        // Trang chủ
        public ActionResult Index()
        {
            return View();
        }

        // Trang giới thiệu
        public ActionResult About()
        {
            ViewBag.Message = "Your application description page.";

            return View();
        }

        // Trang liên hệ
        public ActionResult Contact()
        {
            ViewBag.Message = "Your contact page.";

            return View();
        }

        // Kiểm tra kết nối SQL Server
        public ActionResult KiemTraSQL()
        {
            try
            {
                // Lấy chuỗi kết nối từ Web.config
                var cauHinhKetNoi =
                    ConfigurationManager.ConnectionStrings["eHealthCareDB"];

                // Kiểm tra xem có cấu hình hay không
                if (cauHinhKetNoi == null)
                {
                    return Content(
                        "LOI: Khong tim thay eHealthCareDB trong Web.config");
                }

                string chuoiKetNoi =
                    cauHinhKetNoi.ConnectionString;

                // Kết nối đến SQL Server
                using (SqlConnection conn =
                    new SqlConnection(chuoiKetNoi))
                {
                    conn.Open();

                    // Kiểm tra database đang kết nối
                    using (SqlCommand cmd =
                        new SqlCommand("SELECT DB_NAME()", conn))
                    {
                        string tenDatabase =
                            Convert.ToString(cmd.ExecuteScalar());

                        return Content(
                            "KET NOI SQL THANH CONG! Database: "
                            + tenDatabase);
                    }
                }
            }
            catch (Exception ex)
            {
                // Ghi lỗi vào Output của Visual Studio
                System.Diagnostics.Debug.WriteLine(ex.ToString());

                // Chỉ hiển thị chi tiết khi truy cập từ máy cục bộ
                if (Request.IsLocal)
                {
                    return Content(
                        "LOI SQL CHI TIET: " + ex.Message);
                }

                return Content("Khong the ket noi SQL Server.");
            }
        }
    }
}
