
using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Text.RegularExpressions;
using System.Web.Mvc;

namespace eHealthCare.Controllers
{
    [Authorize]
    public class BenhNhanController : Controller
    {
        private string ChuoiKetNoi
        {
            get
            {
                return ConfigurationManager
                    .ConnectionStrings["eHealthCareDB"]
                    .ConnectionString;
            }
        }

        // Hiển thị trang hoàn thiện hồ sơ
        [HttpGet]
        public ActionResult HoSo()
        {
            if (!(Session["MaNguoiDung"] is long))
            {
                return RedirectToAction("Login", "Account");
            }

            long maNguoiDung =
                (long)Session["MaNguoiDung"];

            using (var conn =
                new SqlConnection(ChuoiKetNoi))
            {
                conn.Open();

                string sql = @"
                    SELECT MaBenhNhan, HoTen, DienThoai
                    FROM dbo.WEB_TAI_KHOAN
                    WHERE MaNguoiDung = @MaNguoiDung";

                using (var cmd =
                    new SqlCommand(sql, conn))
                {
                    cmd.Parameters.Add(
                        "@MaNguoiDung", SqlDbType.BigInt
                    ).Value = maNguoiDung;

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read())
                        {
                            return RedirectToAction(
                                "Login", "Account");
                        }

                        if (!reader.IsDBNull(0))
                        {
                            return RedirectToAction(
                                "NhapThe", "BaoHiem");
                        }

                        ViewBag.HoTen =
                            Convert.ToString(reader["HoTen"]);

                        ViewBag.SoDienThoai =
                            Convert.ToString(reader["DienThoai"]);
                    }
                }
            }

            return View();
        }

        // Lưu hồ sơ bệnh nhân
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult HoSo(
            string HoTen,
            string NgaySinh,
            string DiaChi,
            string SoDienThoai,
            string SDTNguoiNha)
        {
            if (!(Session["MaNguoiDung"] is long))
            {
                return RedirectToAction("Login", "Account");
            }

            HoTen = (HoTen ?? "").Trim();
            DiaChi = (DiaChi ?? "").Trim();
            SoDienThoai = (SoDienThoai ?? "").Trim();
            SDTNguoiNha = (SDTNguoiNha ?? "").Trim();

            DateTime ngaySinh;

            bool ngayHopLe = DateTime.TryParseExact(
                NgaySinh,
                "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out ngaySinh
            );

            if (string.IsNullOrWhiteSpace(HoTen) ||
                string.IsNullOrWhiteSpace(DiaChi) ||
                string.IsNullOrWhiteSpace(SoDienThoai) ||
                string.IsNullOrWhiteSpace(SDTNguoiNha) ||
                !ngayHopLe ||
                ngaySinh.Date > DateTime.Today)
            {
                ViewBag.Error =
                    "Vui lòng nhập đầy đủ thông tin hợp lệ.";
                return View();
            }

            if (HoTen.Length > 100 ||
                DiaChi.Length > 200 ||
                !Regex.IsMatch(SoDienThoai, @"^0[0-9]{9}$") ||
                !Regex.IsMatch(SDTNguoiNha, @"^0[0-9]{9}$"))
            {
                ViewBag.Error =
                    "Vui lòng kiểm tra họ tên, địa chỉ và số điện thoại.";
                return View();
            }

            long maNguoiDung =
                (long)Session["MaNguoiDung"];

            try
            {
                using (var conn =
                    new SqlConnection(ChuoiKetNoi))
                {
                    conn.Open();

                    using (var tran =
                        conn.BeginTransaction(
                            IsolationLevel.Serializable))
                    {
                        try
                        {
                            // Khóa tài khoản để tránh tạo hồ sơ trùng
                            string sqlCheck = @"
                                SELECT MaBenhNhan
                                FROM dbo.WEB_TAI_KHOAN WITH (UPDLOCK, HOLDLOCK)
                                WHERE MaNguoiDung = @MaNguoiDung";

                            using (var cmd =
                                new SqlCommand(sqlCheck, conn, tran))
                            {
                                cmd.Parameters.Add(
                                    "@MaNguoiDung",
                                    SqlDbType.BigInt
                                ).Value = maNguoiDung;

                                object ketQua = cmd.ExecuteScalar();

                                if (ketQua == null ||
                                    ketQua == DBNull.Value &&
                                    !TaiKhoanTonTai(conn, tran, maNguoiDung))
                                {
                                    tran.Rollback();

                                    return RedirectToAction(
                                        "Login", "Account");
                                }

                                if (ketQua != DBNull.Value)
                                {
                                    tran.Commit();

                                    return RedirectToAction(
                                        "NhapThe", "BaoHiem");
                                }
                            }

                            // Tạo hồ sơ bệnh nhân
                            string sqlInsert = @"
                                INSERT INTO dbo.BENH_NHAN
                                (
                                    ho_ten,
                                    ngay_sinh,
                                    dia_chi,
                                    so_dien_thoai,
                                    sdt_nguoi_nha
                                )
                                OUTPUT INSERTED.ma_bn
                                VALUES
                                (
                                    @HoTen,
                                    @NgaySinh,
                                    @DiaChi,
                                    @SoDienThoai,
                                    @SDTNguoiNha
                                )";

                            int maBenhNhan;

                            using (var cmd =
                                new SqlCommand(sqlInsert, conn, tran))
                            {
                                cmd.Parameters.AddWithValue(
                                    "@HoTen", HoTen);

                                cmd.Parameters.Add(
                                    "@NgaySinh", SqlDbType.Date
                                ).Value = ngaySinh.Date;

                                cmd.Parameters.AddWithValue(
                                    "@DiaChi", DiaChi);

                                cmd.Parameters.AddWithValue(
                                    "@SoDienThoai", SoDienThoai);

                                cmd.Parameters.AddWithValue(
                                    "@SDTNguoiNha", SDTNguoiNha);

                                maBenhNhan =
                                    Convert.ToInt32(cmd.ExecuteScalar());
                            }

                            // Liên kết tài khoản với bệnh nhân
                            string sqlUpdate = @"
                                UPDATE dbo.WEB_TAI_KHOAN
                                SET MaBenhNhan = @MaBenhNhan
                                WHERE MaNguoiDung = @MaNguoiDung
                                  AND MaBenhNhan IS NULL";

                            using (var cmd =
                                new SqlCommand(sqlUpdate, conn, tran))
                            {
                                cmd.Parameters.Add(
                                    "@MaBenhNhan", SqlDbType.Int
                                ).Value = maBenhNhan;

                                cmd.Parameters.Add(
                                    "@MaNguoiDung", SqlDbType.BigInt
                                ).Value = maNguoiDung;

                                if (cmd.ExecuteNonQuery() != 1)
                                {
                                    throw new InvalidOperationException(
                                        "Không thể liên kết hồ sơ.");
                                }
                            }

                            tran.Commit();
                        }
                        catch
                        {
                            tran.Rollback();
                            throw;
                        }
                    }
                }

                return RedirectToAction(
                    "NhapThe", "BaoHiem");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);

                ViewBag.Error =
                    "Không thể lưu hồ sơ. Vui lòng kiểm tra dữ liệu hoặc SQL Server.";

                return View();
            }
        }

        private static bool TaiKhoanTonTai(
            SqlConnection conn,
            SqlTransaction tran,
            long maNguoiDung)
        {
            using (var cmd = new SqlCommand(
                "SELECT COUNT(1) FROM dbo.WEB_TAI_KHOAN WHERE MaNguoiDung = @id",
                conn, tran))
            {
                cmd.Parameters.Add(
                    "@id", SqlDbType.BigInt
                ).Value = maNguoiDung;

                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }
    }
}
