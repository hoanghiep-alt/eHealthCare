using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Web.Mvc;

namespace eHealthCare.Controllers
{
    [Authorize]
    public class KhamBenhController : Controller
    {
        private string ChuoiKetNoi
        {
            get
            {
                return ConfigurationManager.ConnectionStrings["eHealthCareDB"].ConnectionString;
            }
        }

        // Lay tai khoan tu cookie dang nhap, khong phu thuoc hoan toan vao Session.
        private long? LayMaNguoiDung(SqlConnection conn, SqlTransaction tran = null)
        {
            if (!User.Identity.IsAuthenticated || String.IsNullOrWhiteSpace(User.Identity.Name))
                return null;

            string sql = @"
    SELECT TOP (5)
        ma_ck,
        ten_chuyen_khoa
    FROM dbo.CHUYEN_KHOA
    ORDER BY ten_chuyen_khoa";

            using (var cmd = new SqlCommand(sql, conn, tran))
            {
                cmd.Parameters.Add("@TenDangNhap", SqlDbType.NVarChar, 50).Value = User.Identity.Name;
                object value = cmd.ExecuteScalar();
                if (value == null || value == DBNull.Value) return null;
                long id = Convert.ToInt64(value);
                Session["MaNguoiDung"] = id;
                return id;
            }
        }


        private List<SelectListItem> LayDanhSachKhoa()
        {
            var danhSach = new List<SelectListItem>();

            using (SqlConnection conn =
                new SqlConnection(ChuoiKetNoi))
            {
                conn.Open();

                string sql = @"
            SELECT
                ma_ck,
                ten_chuyen_khoa
            FROM dbo.CHUYEN_KHOA
            WHERE ten_chuyen_khoa IN
            (
                N'Khoa Mắt',
                N'Khoa Nội Tổng Quát',
                N'Khoa Tim Mạch',
                N'Khoa Ngoại Tổng Hợp',
                N'Khoa Sản Phụ Khoa'
            )
            ORDER BY CASE ten_chuyen_khoa
                WHEN N'Khoa Mắt' THEN 1
                WHEN N'Khoa Nội Tổng Quát' THEN 2
                WHEN N'Khoa Tim Mạch' THEN 3
                WHEN N'Khoa Ngoại Tổng Hợp' THEN 4
                WHEN N'Khoa Sản Phụ Khoa' THEN 5
                ELSE 6
            END";

                using (SqlCommand cmd =
                    new SqlCommand(sql, conn))
                {
                    using (SqlDataReader reader =
                        cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            danhSach.Add(new SelectListItem
                            {
                                Value = reader["ma_ck"].ToString(),
                                Text = reader["ten_chuyen_khoa"].ToString()
                            });
                        }
                    }
                }
            }

            return danhSach;
        }


        // Dieu kien ca truc dung chung cho trang danh sach va luc cap so.
        // Xu ly ca khuya qua ngay moi (VD: 21:00 - 06:00).
        private const string DieuKienDangTruc = @"
            ct.gio_bat_dau IS NOT NULL AND ct.gio_ket_thuc IS NOT NULL
            AND (
                (ct.gio_bat_dau < ct.gio_ket_thuc
                 AND lt.ngay_truc = CONVERT(date, GETDATE())
                 AND CONVERT(time, GETDATE()) >= ct.gio_bat_dau
                 AND CONVERT(time, GETDATE()) < ct.gio_ket_thuc)
                OR
                (ct.gio_bat_dau > ct.gio_ket_thuc
                 AND lt.ngay_truc = CONVERT(date, GETDATE())
                 AND CONVERT(time, GETDATE()) >= ct.gio_bat_dau)
                OR
                (ct.gio_bat_dau > ct.gio_ket_thuc
                 AND lt.ngay_truc = DATEADD(day, -1, CONVERT(date, GETDATE()))
                 AND CONVERT(time, GETDATE()) < ct.gio_ket_thuc)
            )";

        private List<SelectListItem> LayDanhSachBacSi(int maKhoa)
        {
            var result = new List<SelectListItem>();
            using (var conn = new SqlConnection(ChuoiKetNoi))
            {
                conn.Open();
                string sql = @"
                   SELECT DISTINCT TOP (4)
    bs.ma_bs,
    bs.ho_ten
                    FROM dbo.BAC_SI bs
                    JOIN dbo.LICH_TRUC lt ON lt.ma_bs = bs.ma_bs
                    JOIN dbo.CA_TRUC ct ON ct.ma_ca = lt.ma_ca
                   WHERE bs.ma_ck = @MaKhoa
  AND bs.DangHoatDong = 1
                    ORDER BY bs.ho_ten";
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.Add("@MaKhoa", SqlDbType.Int).Value = maKhoa;
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                            result.Add(new SelectListItem
                            {
                                Value = reader["ma_bs"].ToString(),
                                Text = reader["ho_ten"].ToString()
                            });
                    }
                }
            }
            return result;
        }

        [HttpGet]
        public ActionResult ChonKhoa()
        {
            if (!Equals(Session["BHYT_DaNhap"], true))
                return RedirectToAction("NhapThe", "BaoHiem");

            ViewBag.DanhSach = LayDanhSachKhoa();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ChonKhoa(int? ma_ck)
        {
            if (!Equals(Session["BHYT_DaNhap"], true))
                return RedirectToAction("NhapThe", "BaoHiem");

            var danhSach = LayDanhSachKhoa();
            if (!ma_ck.HasValue || !danhSach.Any(x => x.Value == ma_ck.Value.ToString()))
            {
                ViewBag.Error = "Vui lòng chọn chuyên khoa hợp lệ.";
                ViewBag.DanhSach = danhSach;
                return View();
            }
            var khoa = danhSach.First(x => x.Value == ma_ck.Value.ToString());
            Session["MaChuyenKhoa"] = ma_ck.Value;
            Session["TenChuyenKhoa"] = khoa.Text;
            Session.Remove("MaBacSi");
            Session.Remove("TenBacSi");
            return RedirectToAction("ChonBacSi");
        }

        [HttpGet]
        public ActionResult ChonBacSi()
        {
            if (!Equals(Session["BHYT_DaNhap"], true))
                return RedirectToAction("NhapThe", "BaoHiem");
            if (!(Session["MaChuyenKhoa"] is int))
                return RedirectToAction("ChonKhoa");

            int maKhoa = (int)Session["MaChuyenKhoa"];
            ViewBag.TenKhoa = Session["TenChuyenKhoa"];
            ViewBag.DanhSach = LayDanhSachBacSi(maKhoa);
            ViewBag.ThongBao = TempData["ThongBao"];
            ViewBag.Error = TempData["LoiLaySo"];
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ChonBacSi(int? ma_bs)
        {




            if (!Equals(Session["BHYT_DaNhap"], true))
            {
                return RedirectToAction("NhapThe", "BaoHiem");
            }

            if (!(Session["MaChuyenKhoa"] is int))
            {
                return RedirectToAction("ChonKhoa");
            }

            if (!ma_bs.HasValue)
            {
                TempData["LoiLaySo"] = "Vui lòng chọn bác sĩ.";
                return RedirectToAction("ChonBacSi");
            }


            int maKhoa = (int)Session["MaChuyenKhoa"];
            try
            {
                using (var conn = new SqlConnection(ChuoiKetNoi))
                {
                    conn.Open();
                    using (var tran = conn.BeginTransaction(IsolationLevel.Serializable))
                    {
                        try
                        {

                            // Lấy đúng tài khoản đang đăng nhập
                            string tenDangNhap = User.Identity.IsAuthenticated
                                ? User.Identity.Name
                                : "";

                            if (string.IsNullOrWhiteSpace(tenDangNhap))
                            {
                                tran.Rollback();
                                return RedirectToAction("Login", "Account");
                            }

                            int maBenhNhan;
                            long maNguoiDung;
                            bool laBacSi;

                            string sqlTaiKhoan = @"
    SELECT TOP (1)
        tk.MaNguoiDung,
        tk.MaBenhNhan,
        CASE
            WHEN EXISTS (
                SELECT 1
                FROM dbo.WEB_BAC_SI_TAI_KHOAN bs
                WHERE bs.MaNguoiDung = tk.MaNguoiDung
            ) THEN 1
            ELSE 0
        END AS LaBacSi
    FROM dbo.WEB_TAI_KHOAN tk
    WHERE tk.TenDangNhap = @TenDangNhap";

                            using (var cmd = new SqlCommand(
                                sqlTaiKhoan, conn, tran))
                            {
                                cmd.Parameters.Add(
                                    "@TenDangNhap",
                                    SqlDbType.NVarChar, 50
                                ).Value = tenDangNhap;

                                using (var reader = cmd.ExecuteReader())
                                {
                                    if (!reader.Read())
                                    {
                                        reader.Close();
                                        tran.Rollback();
                                        return RedirectToAction("Login", "Account");
                                    }

                                    maNguoiDung = Convert.ToInt64(
                                        reader["MaNguoiDung"]);

                                    laBacSi = Convert.ToInt32(
                                        reader["LaBacSi"]) == 1;

                                    maBenhNhan = reader["MaBenhNhan"] == DBNull.Value
                                        ? 0
                                        : Convert.ToInt32(reader["MaBenhNhan"]);
                                }
                            }

                            // Không cho tài khoản bác sĩ lấy số bệnh nhân
                            if (laBacSi)
                            {
                                tran.Rollback();

                                return new HttpStatusCodeResult(
                                    403,
                                    "Tài khoản bác sĩ không được lấy số khám.");
                            }

                            if (maBenhNhan <= 0)
                            {
                                tran.Rollback();
                                return RedirectToAction("HoSo", "BenhNhan");
                            }

                            // Đồng bộ Session theo đúng tài khoản đăng nhập
                            Session["MaNguoiDung"] = maNguoiDung;


                            // Kiem tra lai bac si dang truc ngay tai thoi diem nhan nut.
                            // Ngay ca truc duoc luu de ho tro ca khuya qua nua dem.
                            int maLichTruc;
                            DateTime ngayTruc;
                            string sqlTruc = @"
                                SELECT TOP (1) lt.ma_lich_truc, lt.ngay_truc
                                FROM dbo.LICH_TRUC lt
                                JOIN dbo.BAC_SI bs ON lt.ma_bs = bs.ma_bs
                                JOIN dbo.CA_TRUC ct ON lt.ma_ca = ct.ma_ca
                                WHERE bs.ma_bs = @MaBacSi
                                  AND bs.ma_ck = @MaKhoa
                                  AND " + DieuKienDangTruc + @"
                                AND bs.DangHoatDong = 1
                                ORDER BY lt.ma_lich_truc";
                            using (var cmd = new SqlCommand(sqlTruc, conn, tran))
                            {
                                cmd.Parameters.Add("@MaBacSi", SqlDbType.Int).Value = ma_bs.Value;
                                cmd.Parameters.Add("@MaKhoa", SqlDbType.Int).Value = maKhoa;
                                using (var reader = cmd.ExecuteReader())
                                {
                                    if (!reader.Read())
                                    {
                                        reader.Close();
                                        tran.Rollback();
                                        TempData["LoiLaySo"] = "Bác sĩ không còn trong ca trực. Vui lòng chọn lại.";
                                        return RedirectToAction("ChonBacSi");
                                    }
                                    maLichTruc = Convert.ToInt32(reader["ma_lich_truc"]);
                                    ngayTruc = Convert.ToDateTime(reader["ngay_truc"]);
                                }
                            }

                            // Mot khoa cap so chung cho moi luot trong cung lich truc.
                            using (var cmd = new SqlCommand(@"
                                DECLARE @KetQua int;
                                EXEC @KetQua = sys.sp_getapplock
                                    @Resource = @Khoa,
                                    @LockMode = 'Exclusive',
                                    @LockOwner = 'Transaction',
                                    @LockTimeout = 10000;
                                SELECT @KetQua;", conn, tran))
                            {
                                cmd.Parameters.Add("@Khoa", SqlDbType.NVarChar, 255)
                                   .Value = "eHealthCare_LuotKham_" + maLichTruc;
                                if (Convert.ToInt32(cmd.ExecuteScalar()) < 0)
                                    throw new InvalidOperationException("Không lấy được khóa cấp số.");
                            }

                            // Mot benh nhan khong nhan hai so trong mot ca truc.
                            using (var cmd = new SqlCommand(@"
                                SELECT TOP (1) ma_luot
                                FROM dbo.LUOT_KHAM
                                WHERE ma_bn = @MaBN AND ma_lich_truc = @MaLT
                                ORDER BY ma_luot DESC", conn, tran))
                            {
                                cmd.Parameters.Add("@MaBN", SqlDbType.Int).Value = maBenhNhan;
                                cmd.Parameters.Add("@MaLT", SqlDbType.Int).Value = maLichTruc;
                                object existed = cmd.ExecuteScalar();
                                if (existed != null)
                                {
                                    int maCu = Convert.ToInt32(existed);
                                    tran.Commit();

                                    return RedirectToAction(
                                        "PhieuSo",
                                        "KhamBenh",
                                        new { id = maCu }
                                    );
                                }
                            }

                            int soThuTu;
                            using (var cmd = new SqlCommand(@"
                                SELECT ISNULL(MAX(so_thu_tu), 0) + 1
                                FROM dbo.LUOT_KHAM
                                WHERE ma_lich_truc = @MaLT", conn, tran))
                            {
                                cmd.Parameters.Add("@MaLT", SqlDbType.Int).Value = maLichTruc;
                                soThuTu = Convert.ToInt32(cmd.ExecuteScalar());
                            }

                            // Phai dung dung gia tri trang thai duoc CHECK chuan nhan.

                            const string trangThaiMoi = "Chờ khám";

                            int maMoi;
                            using (var cmd = new SqlCommand(@"
                                INSERT INTO dbo.LUOT_KHAM
                                    (ma_bn, ma_lich_truc, so_thu_tu, ngay_kham, trang_thai)
                                OUTPUT INSERTED.ma_luot
                                VALUES (@MaBN, @MaLT, @STT, @NgayKham, @TrangThai)", conn, tran))
                            {
                                cmd.Parameters.Add("@MaBN", SqlDbType.Int).Value = maBenhNhan;
                                cmd.Parameters.Add("@MaLT", SqlDbType.Int).Value = maLichTruc;
                                cmd.Parameters.Add("@STT", SqlDbType.Int).Value = soThuTu;
                                // Ca khuya lay ngay bat dau ca, giu thong nhat ngay kham theo ca.
                                cmd.Parameters.Add("@NgayKham", SqlDbType.Date).Value = ngayTruc.Date;
                                cmd.Parameters.Add("@TrangThai", SqlDbType.NVarChar, 20).Value = trangThaiMoi;
                                maMoi = Convert.ToInt32(cmd.ExecuteScalar());
                            }

                            tran.Commit();

                            return RedirectToAction(
                                "PhieuSo",
                                "KhamBenh",
                                new { id = maMoi }
                            );
                        }
                        catch
                        {
                            if (tran.Connection != null) tran.Rollback();
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("LOI CAP SO KHAM: " + ex);
                TempData["LoiLaySo"] = Request.IsLocal
                    ? "Lỗi SQL: " + ex.Message
                    : "Chưa thể cấp số khám, vui lòng liên hệ nhân viên hỗ trợ.";
                return RedirectToAction("ChonBacSi");
            }
        }

       

        [HttpGet]
        [Authorize]
        public ActionResult PhieuSo(int? id)
        {
            // Kiểm tra mã lượt khám
            if (!id.HasValue || id.Value <= 0)
            {
                return new HttpStatusCodeResult(
                    400, "Thiếu mã lượt khám.");
            }

            // Lấy đúng tên tài khoản đang đăng nhập
            string tenDangNhap = User.Identity.IsAuthenticated
                ? User.Identity.Name
                : "";

            if (string.IsNullOrWhiteSpace(tenDangNhap))
            {
                return RedirectToAction("Login", "Account");
            }

            using (SqlConnection conn =
                new SqlConnection(ChuoiKetNoi))
            {
                conn.Open();

                // Chỉ lấy phiếu thuộc bệnh nhân
                // đang đăng nhập
                string sql = @"
            SELECT TOP (1)
                lk.ma_luot,
                lk.so_thu_tu,
                lk.ngay_kham,
                lk.trang_thai,

                bn.ho_ten AS TenBenhNhan,
                bs.ho_ten AS TenBacSi,
                ck.ten_chuyen_khoa AS TenKhoa,
                ct.ten_ca AS TenCa

            FROM dbo.LUOT_KHAM lk

            INNER JOIN dbo.BENH_NHAN bn
                ON lk.ma_bn = bn.ma_bn

            INNER JOIN dbo.WEB_TAI_KHOAN tk
                ON tk.MaBenhNhan = bn.ma_bn

            INNER JOIN dbo.LICH_TRUC lt
                ON lk.ma_lich_truc = lt.ma_lich_truc

            INNER JOIN dbo.BAC_SI bs
                ON lt.ma_bs = bs.ma_bs

            INNER JOIN dbo.CHUYEN_KHOA ck
                ON bs.ma_ck = ck.ma_ck

            INNER JOIN dbo.CA_TRUC ct
                ON lt.ma_ca = ct.ma_ca

            WHERE lk.ma_luot = @MaLuot
              AND tk.TenDangNhap = @TenDangNhap";

                using (SqlCommand cmd =
                    new SqlCommand(sql, conn))
                {
                    cmd.Parameters.Add(
                        "@MaLuot",
                        SqlDbType.Int
                    ).Value = id.Value;

                    cmd.Parameters.Add(
                        "@TenDangNhap",
                        SqlDbType.NVarChar, 50
                    ).Value = tenDangNhap;

                    using (SqlDataReader reader =
                        cmd.ExecuteReader())
                    {
                        if (!reader.Read())
                        {
                            return HttpNotFound(
                                "Không tìm thấy phiếu khám " +
                                "thuộc tài khoản này.");
                        }

                        ViewBag.MaLuot =
                            Convert.ToInt32(
                                reader["ma_luot"]);

                        ViewBag.SoThuTu =
                            Convert.ToInt32(
                                reader["so_thu_tu"]);

                        ViewBag.NgayKham =
                            Convert.ToDateTime(
                                reader["ngay_kham"]);

                        ViewBag.TrangThai =
                            Convert.ToString(
                                reader["trang_thai"]);

                        ViewBag.TenBenhNhan =
                            Convert.ToString(
                                reader["TenBenhNhan"]);

                        ViewBag.TenBacSi =
                            Convert.ToString(
                                reader["TenBacSi"]);

                        ViewBag.TenKhoa =
                            Convert.ToString(
                                reader["TenKhoa"]);

                        ViewBag.TenCa =
                            Convert.ToString(
                                reader["TenCa"]);
                    }
                }
            }

            return View();
        }

    }
}