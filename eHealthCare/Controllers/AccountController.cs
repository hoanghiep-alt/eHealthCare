
using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Web.Mvc;
using System.Web.Security;


namespace eHealthCare.Controllers
{
    public class AccountController : Controller
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

        // ==========================
        // TRANG ĐĂNG KÝ
        // ==========================
        [HttpGet]
        public ActionResult Register()
        {
            return View();
        }

        // Xử lý đăng ký tài khoản
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Register(
            string FullName,
            string TenDangNhap,
            string PhoneNumber,
            string Password,
            string ConfirmPassword)
        {
            FullName = (FullName ?? "").Trim();
            TenDangNhap = (TenDangNhap ?? "").Trim();
            PhoneNumber = (PhoneNumber ?? "").Trim();

            if (string.IsNullOrWhiteSpace(FullName) ||
                string.IsNullOrWhiteSpace(TenDangNhap) ||
                string.IsNullOrWhiteSpace(PhoneNumber) ||
                string.IsNullOrEmpty(Password))
            {
                ViewBag.Error = "Vui lòng nhập đầy đủ thông tin.";
                return View();
            }

            if (FullName.Length > 100)
            {
                ViewBag.Error = "Họ tên không được quá 100 ký tự.";
                return View();
            }

            if (!Regex.IsMatch(
                TenDangNhap, @"^[a-zA-Z0-9._]{4,30}$"))
            {
                ViewBag.Error =
                    "Tên đăng nhập phải có 4-30 ký tự, " +
                    "chỉ gồm chữ không dấu, số, dấu chấm hoặc gạch dưới.";

                return View();
            }

            if (!Regex.IsMatch(
                PhoneNumber, @"^0[0-9]{9}$"))
            {
                ViewBag.Error =
                    "Số điện thoại phải có 10 chữ số và bắt đầu bằng 0.";

                return View();
            }

            if (Password.Length < 8 ||
                Password.Length > 128)
            {
                ViewBag.Error =
                    "Mật khẩu phải có từ 8 đến 128 ký tự.";

                return View();
            }

            if (Password != ConfirmPassword)
            {
                ViewBag.Error =
                    "Mật khẩu xác nhận không khớp.";

                return View();
            }

            try
            {
                using (SqlConnection conn =
                    new SqlConnection(ChuoiKetNoi))
                {
                    conn.Open();

                    // Kiểm tra trùng tên đăng nhập
                    string sqlCheck = @"
                        SELECT COUNT(1)
                        FROM dbo.WEB_TAI_KHOAN
                        WHERE TenDangNhap = @TenDangNhap";

                    using (SqlCommand cmd =
                        new SqlCommand(sqlCheck, conn))
                    {
                        cmd.Parameters.Add(
                            "@TenDangNhap",
                            SqlDbType.NVarChar, 50
                        ).Value = TenDangNhap;

                        int count =
                            Convert.ToInt32(cmd.ExecuteScalar());

                        if (count > 0)
                        {
                            ViewBag.Error =
                                "Tên đăng nhập đã tồn tại.";

                            return View();
                        }
                    }

                    // Băm mật khẩu trước khi lưu
                    string hash =
                        TaoHashMatKhau(Password);

                    // Lưu tài khoản vào SQL
                    string sqlInsert = @"
                        INSERT INTO dbo.WEB_TAI_KHOAN
                        (
                            HoTen,
                            TenDangNhap,
                            DienThoai,
                            MatKhauHash
                        )
                        VALUES
                        (
                            @HoTen,
                            @TenDangNhap,
                            @DienThoai,
                            @MatKhauHash
                        )";

                    using (SqlCommand cmd =
                        new SqlCommand(sqlInsert, conn))
                    {
                        cmd.Parameters.Add(
                            "@HoTen",
                            SqlDbType.NVarChar, 100
                        ).Value = FullName;

                        cmd.Parameters.Add(
                            "@TenDangNhap",
                            SqlDbType.NVarChar, 50
                        ).Value = TenDangNhap;

                        cmd.Parameters.Add(
                            "@DienThoai",
                            SqlDbType.VarChar, 20
                        ).Value = PhoneNumber;

                        cmd.Parameters.Add(
                            "@MatKhauHash",
                            SqlDbType.NVarChar, 500
                        ).Value = hash;

                        cmd.ExecuteNonQuery();
                    }
                }

                TempData["ThongBao"] =
                    "Đăng ký thành công! Hãy đăng nhập.";

                return RedirectToAction("Login");
            }
            catch (SqlException ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);

                if (ex.Number == 2601 ||
                    ex.Number == 2627)
                {
                    ViewBag.Error =
                        "Tên đăng nhập đã tồn tại.";
                }
                else
                {
                    ViewBag.Error =
                        "Không thể đăng ký tài khoản. " +
                        "Vui lòng kiểm tra SQL Server.";
                }

                return View();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);

                ViewBag.Error =
                    "Đã xảy ra lỗi khi đăng ký.";

                return View();
            }
        }

        // ==========================
        // TRANG ĐĂNG NHẬP
        // ==========================
        // Tìm vai trò từ SQL, KHÔNG nhận vai trò từ form hay Session.
        // Trả về: BacSi, BenhNhan, hoặc ChuaCoHoSo.

        private string LayVaiTro(long maNguoiDung, SqlConnection conn)
        {
            const string sql = @"
        SELECT
            tk.MaBenhNhan,

            CASE WHEN EXISTS
            (
                SELECT 1
                FROM dbo.WEB_BAC_SI_TAI_KHOAN b
                WHERE b.MaNguoiDung = tk.MaNguoiDung
            ) THEN 1 ELSE 0 END AS LaBacSi,

            CASE WHEN EXISTS
            (
                SELECT 1
                FROM dbo.WEB_NHA_THUOC_TAI_KHOAN n
                WHERE n.MaNguoiDung = tk.MaNguoiDung
            ) THEN 1 ELSE 0 END AS LaNhaThuoc,

            CASE WHEN EXISTS
            (
                SELECT 1
                FROM dbo.WEB_THU_NGAN_TAI_KHOAN t
                WHERE t.MaNguoiDung = tk.MaNguoiDung
            ) THEN 1 ELSE 0 END AS LaThuNgan

        FROM dbo.WEB_TAI_KHOAN tk
        WHERE tk.MaNguoiDung = @MaNguoiDung";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.Add(
                    "@MaNguoiDung", SqlDbType.BigInt
                ).Value = maNguoiDung;

                using (var reader = cmd.ExecuteReader())
                {
                    if (!reader.Read())
                        return null;

                    bool laBacSi =
                        Convert.ToInt32(reader["LaBacSi"]) == 1;

                    bool laNhaThuoc =
                        Convert.ToInt32(reader["LaNhaThuoc"]) == 1;

                    bool laThuNgan =
                        Convert.ToInt32(reader["LaThuNgan"]) == 1;

                    bool laBenhNhan =
                        reader["MaBenhNhan"] != DBNull.Value;

                    // Ngăn một tài khoản bị gán nhiều vai trò
                    int soVaiTro =
                        (laBacSi ? 1 : 0) +
                        (laNhaThuoc ? 1 : 0) +
                        (laThuNgan ? 1 : 0) +
                        (laBenhNhan ? 1 : 0);

                    if (soVaiTro > 1)
                        return "XungDot";

                    if (laThuNgan)
                        return "ThuNgan";

                    if (laNhaThuoc)
                        return "NhaThuoc";

                    if (laBacSi)
                        return "BacSi";

                    if (laBenhNhan)
                        return "BenhNhan";

                    return "ChuaCoHoSo";
                }
            }
        }



        private ActionResult ChuyenTrangTheoVaiTro(string vaiTro)
        {
            // Tài khoản thu ngân
            if (vaiTro == "ThuNgan")
            {
                return RedirectToAction(
                    "DanhSach", "ThuNgan");
            }

            // Tài khoản nhà thuốc
            if (vaiTro == "NhaThuoc")
            {
                return RedirectToAction(
                    "DanhSachDon", "NhaThuoc");
            }

            // Tài khoản bác sĩ
            if (vaiTro == "BacSi")
            {
                return RedirectToAction(
                    "DanhSachCho", "BacSi");
            }

            // Tài khoản bệnh nhân
            if (vaiTro == "BenhNhan")
            {
                return RedirectToAction(
                    "NhapThe", "BaoHiem");
            }

            // Bệnh nhân mới chưa hoàn thành hồ sơ
            if (vaiTro == "ChuaCoHoSo")
            {
                return RedirectToAction(
                    "HoSo", "BenhNhan");
            }

            // Tài khoản bị xung đột quyền
            FormsAuthentication.SignOut();
            Session.Clear();
            Session.Abandon();

            ViewBag.Error =
                "Tài khoản chưa được cấu hình hợp lệ. " +
                "Vui lòng liên hệ quản trị viên.";

            return View("Login");
        }


        [HttpGet]
        public ActionResult Login()
        {
            if (User.Identity.IsAuthenticated)
            {
                try
                {
                    using (var conn = new SqlConnection(ChuoiKetNoi))
                    {
                        conn.Open();
                        const string sql = @"
                            SELECT MaNguoiDung
                            FROM dbo.WEB_TAI_KHOAN
                            WHERE TenDangNhap = @TenDangNhap";
                        using (var cmd = new SqlCommand(sql, conn))
                        {
                            cmd.Parameters.Add("@TenDangNhap", SqlDbType.NVarChar, 50)
                               .Value = User.Identity.Name;
                            object result = cmd.ExecuteScalar();
                            if (result != null && result != DBNull.Value)
                            {
                                long id = Convert.ToInt64(result);
                                Session["MaNguoiDung"] = id;
                                return ChuyenTrangTheoVaiTro(LayVaiTro(id, conn));
                            }
                        }
                    }
                    FormsAuthentication.SignOut();
                    Session.Clear();
                    Session.Abandon();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(ex);
                    ViewBag.Error = "Không thể kiểm tra tài khoản. Vui lòng thử lại.";
                }
            }
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Login(string TenDangNhap, string Password)
        {
            TenDangNhap = (TenDangNhap ?? "").Trim();
            if (string.IsNullOrWhiteSpace(TenDangNhap) ||
                string.IsNullOrEmpty(Password))
            {
                ViewBag.Error = "Vui lòng nhập tên đăng nhập và mật khẩu.";
                return View();
            }
            if (TenDangNhap.Length > 50)
            {
                ViewBag.Error = "Tên đăng nhập hoặc mật khẩu không đúng.";
                return View();
            }

            try
            {
                long maNguoiDung = 0;
                string matKhauHash = null;
                string vaiTro;

                using (var conn = new SqlConnection(ChuoiKetNoi))
                {
                    conn.Open();
                    const string sql = @"
                        SELECT TOP (1) MaNguoiDung, MatKhauHash
                        FROM dbo.WEB_TAI_KHOAN
                        WHERE TenDangNhap = @TenDangNhap";
                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.Add("@TenDangNhap", SqlDbType.NVarChar, 50)
                           .Value = TenDangNhap;
                        using (var r = cmd.ExecuteReader())
                        {
                            if (r.Read())
                            {
                                maNguoiDung = Convert.ToInt64(r["MaNguoiDung"]);
                                matKhauHash = Convert.ToString(r["MatKhauHash"]);
                            }
                        }
                    }
                    if (matKhauHash == null ||
                        !KiemTraMatKhau(Password, matKhauHash))
                    {
                        ViewBag.Error = "Tên đăng nhập hoặc mật khẩu không đúng.";
                        return View();
                    }

                    vaiTro = LayVaiTro(maNguoiDung, conn);
                }

                if (vaiTro == null || vaiTro == "XungDot")
                {
                    ViewBag.Error = "Tài khoản chưa được cấu hình hợp lệ. Vui lòng liên hệ quản trị viên.";
                    return View();
                }

                // Dọn dữ liệu phiên của lần đăng nhập trước (nếu còn).
                Session.Clear();
                Session["MaNguoiDung"] = maNguoiDung;
                FormsAuthentication.SetAuthCookie(TenDangNhap, false);
                return ChuyenTrangTheoVaiTro(vaiTro);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                ViewBag.Error = "Không thể đăng nhập. Vui lòng thử lại.";
                return View();
            }
        }

        private ActionResult XuLyDangXuat()
        {
            FormsAuthentication.SignOut();
            Session.Clear();
            Session.Abandon();
            Response.Cache.SetCacheability(System.Web.HttpCacheability.NoCache);
            Response.Cache.SetNoStore();
            return RedirectToAction("Login", "Account");
        }

        // Giữ cả hai tên endpoint để các form cũ vẫn sử dụng được.
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public ActionResult Logout()
        {
            return XuLyDangXuat();
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public ActionResult DangXuat()
        {
            FormsAuthentication.SignOut();

            Session.Clear();
            Session.Abandon();

            Response.Cache.SetCacheability(
                System.Web.HttpCacheability.NoCache);

            Response.Cache.SetNoStore();

            return RedirectToAction("Login", "Account");
        }

        // ==========================
        // QUÊN MẬT KHẨU
        // ==========================
        [HttpGet]
        public ActionResult ForgotPassword()
        {
            return View();
        }

        // ==========================
        // TẠO HASH MẬT KHẨU
        // ==========================
        private static string TaoHashMatKhau(
            string password)
        {
            byte[] salt = new byte[16];

            using (RandomNumberGenerator rng =
                RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }

            using (Rfc2898DeriveBytes pbkdf2 =
                new Rfc2898DeriveBytes(
                    password,
                    salt,
                    150000,
                    HashAlgorithmName.SHA256))
            {
                byte[] hash = pbkdf2.GetBytes(32);

                return "PBKDF2-SHA256$150000$"
                    + Convert.ToBase64String(salt)
                    + "$"
                    + Convert.ToBase64String(hash);
            }
        }

        // ==========================
        // KIỂM TRA MẬT KHẨU
        // ==========================
        private static bool KiemTraMatKhau(
            string password,
            string storedHash)
        {
            try
            {
                string[] parts =
                    storedHash.Split('$');

                if (parts.Length != 4 ||
                    parts[0] != "PBKDF2-SHA256")
                {
                    return false;
                }

                int iterations;

                if (!int.TryParse(
                    parts[1], out iterations))
                {
                    return false;
                }

                if (iterations < 10000 ||
                    iterations > 1000000)
                {
                    return false;
                }

                byte[] salt =
                    Convert.FromBase64String(parts[2]);

                byte[] savedHash =
                    Convert.FromBase64String(parts[3]);

                if (salt.Length != 16 ||
                    savedHash.Length != 32)
                {
                    return false;
                }

                using (Rfc2898DeriveBytes pbkdf2 =
                    new Rfc2898DeriveBytes(
                        password,
                        salt,
                        iterations,
                        HashAlgorithmName.SHA256))
                {
                    byte[] newHash =
                        pbkdf2.GetBytes(savedHash.Length);

                    int diff = 0;

                    for (int i = 0;
                         i < savedHash.Length; i++)
                    {
                        diff |=
                            savedHash[i] ^ newHash[i];
                    }

                    return diff == 0;
                }
            }
            catch
            {
                return false;
            }
        }


    }
}
