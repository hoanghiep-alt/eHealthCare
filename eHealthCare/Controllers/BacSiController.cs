using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;

namespace eHealthCare.Controllers
{
    public class BacSiTrucItem
    {
        public int MaBacSi { get; set; }
        public string TenBacSi { get; set; }
        public string TenKhoa { get; set; }
        public int SoCho { get; set; }
    }

    public class BenhNhanChoItem
    {
        public int MaLuot { get; set; }
        public int SoThuTu { get; set; }
        public string TenBenhNhan { get; set; }
        public string TrangThai { get; set; }
        public string TenCa { get; set; }
        public DateTime NgayKham { get; set; }
    }

    public class DanhSachChoViewModel
    {
        public int MaBacSi { get; set; }
        public string TenBacSi { get; set; }
        public string TenKhoa { get; set; }
        // Chỉ tài khoản gắn với chính bác sĩ này được ghi chẩn đoán.
        public bool CoQuyenKham { get; set; }
        public List<BenhNhanChoItem> DanhSach { get; set; }

        public DanhSachChoViewModel()
        {
            DanhSach = new List<BenhNhanChoItem>();
        }
    }

    // Chỉ dùng các cột có thật trong bảng PHIEU_KHAM.
    public class KhamBenhViewModel
    {
        public int MaLuot { get; set; }
        public int MaBacSi { get; set; }
        public int SoThuTu { get; set; }
        public DateTime NgayKham { get; set; }
        public string TenBenhNhan { get; set; }
        public string TenBacSi { get; set; }
        public string TenKhoa { get; set; }
        public string BenhChanDoan { get; set; }
    }

    [Authorize]
    public class BacSiController : Controller
    {
        private string ChuoiKetNoi
        {
            get
            {
                return ConfigurationManager
                    .ConnectionStrings["eHealthCareDB"].ConnectionString;
            }
        }

        // Dùng chung cho tất cả truy vấn ca trực; hỗ trợ ca qua nửa đêm.
        private const string DieuKienDangTruc = @"
            ct.gio_bat_dau IS NOT NULL
            AND ct.gio_ket_thuc IS NOT NULL
            AND
            (
                (
                    ct.gio_bat_dau < ct.gio_ket_thuc
                    AND lt.ngay_truc = CAST(GETDATE() AS DATE)
                    AND CAST(GETDATE() AS TIME) >= ct.gio_bat_dau
                    AND CAST(GETDATE() AS TIME) < ct.gio_ket_thuc
                )
                OR
                (
                    ct.gio_bat_dau > ct.gio_ket_thuc
                    AND lt.ngay_truc = CAST(GETDATE() AS DATE)
                    AND CAST(GETDATE() AS TIME) >= ct.gio_bat_dau
                )
                OR
                (
                    ct.gio_bat_dau > ct.gio_ket_thuc
                    AND lt.ngay_truc = DATEADD(DAY, -1, CAST(GETDATE() AS DATE))
                    AND CAST(GETDATE() AS TIME) < ct.gio_ket_thuc
                )
            )";

        private class QuyenNhanVien
        {
            public int? MaBacSi { get; set; }
            public bool LaDieuPhoi { get; set; }

            public bool DuocXem(int maBacSi)
            {
                return LaDieuPhoi || (MaBacSi.HasValue && MaBacSi.Value == maBacSi);
            }
        }

        private void KhongLuuCache()
        {
            Response.Cache.SetCacheability(System.Web.HttpCacheability.NoCache);
            Response.Cache.SetNoStore();
            Response.Cache.SetExpires(DateTime.UtcNow.AddMinutes(-1));
        }

        // Đọc quyền từ SQL, tuyệt đối không dựa vào ma_bs do trình duyệt tự gửi.
        private QuyenNhanVien LayQuyen(SqlConnection conn, SqlTransaction tran = null)
        {
            if (User.Identity == null || !User.Identity.IsAuthenticated)
                return null;

            const string sql = @"
                SELECT TOP (1)
                    tk.MaBenhNhan,
                    lk.MaBacSi,
                    CASE WHEN dp.MaNguoiDung IS NULL THEN 0 ELSE 1 END AS LaDieuPhoi
                FROM dbo.WEB_TAI_KHOAN tk
                LEFT JOIN dbo.WEB_BAC_SI_TAI_KHOAN lk
                    ON lk.MaNguoiDung = tk.MaNguoiDung
                LEFT JOIN dbo.WEB_DIEU_PHOI_TAI_KHOAN dp
                    ON dp.MaNguoiDung = tk.MaNguoiDung
                WHERE tk.TenDangNhap = @TenDangNhap";

            using (var cmd = new SqlCommand(sql, conn, tran))
            {
                cmd.Parameters.Add("@TenDangNhap", SqlDbType.NVarChar, 50)
                    .Value = User.Identity.Name;

                using (var reader = cmd.ExecuteReader())
                {
                    if (!reader.Read()) return null;

                    // Tài khoản đã gắn bệnh nhân không được truy cập trang nhân viên.
                    if (reader["MaBenhNhan"] != DBNull.Value) return null;

                    int? maBacSi = reader["MaBacSi"] == DBNull.Value
                        ? (int?)null
                        : Convert.ToInt32(reader["MaBacSi"]);
                    bool laDieuPhoi = Convert.ToInt32(reader["LaDieuPhoi"]) == 1;
                    if (!maBacSi.HasValue && !laDieuPhoi) return null;

                    return new QuyenNhanVien
                    {
                        MaBacSi = maBacSi,
                        LaDieuPhoi = laDieuPhoi
                    };
                }
            }
        }

        // Trang 1: xem tất cả bác sĩ đang trực mà tài khoản được phép quản lý.
        [HttpGet]
        public ActionResult ChonBacSiTruc()
        {
            KhongLuuCache();
            var model = new List<BacSiTrucItem>();

            using (var conn = new SqlConnection(ChuoiKetNoi))
            {
                conn.Open();
                QuyenNhanVien quyen = LayQuyen(conn);
                if (quyen == null)
                    return new HttpStatusCodeResult(403, "Tài khoản không có quyền nhân viên.");

                string sql = @"
                    SELECT bs.ma_bs, bs.ho_ten AS TenBacSi,
                           ck.ten_chuyen_khoa AS TenKhoa,
                           COUNT(DISTINCT lk.ma_luot) AS SoCho
                    FROM dbo.BAC_SI bs
                    INNER JOIN dbo.CHUYEN_KHOA ck ON ck.ma_ck = bs.ma_ck
                    INNER JOIN dbo.LICH_TRUC lt ON lt.ma_bs = bs.ma_bs
                    INNER JOIN dbo.CA_TRUC ct ON ct.ma_ca = lt.ma_ca
                    LEFT JOIN dbo.LUOT_KHAM lk
                        ON lk.ma_lich_truc = lt.ma_lich_truc
                       AND lk.trang_thai = N'Chờ khám'
                    WHERE ( @LaDieuPhoi = 1 OR bs.ma_bs = @MaBacSi )
                      AND " + DieuKienDangTruc + @"
                    GROUP BY bs.ma_bs, bs.ho_ten, ck.ten_chuyen_khoa
                    ORDER BY ck.ten_chuyen_khoa, bs.ho_ten";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.Add("@LaDieuPhoi", SqlDbType.Bit).Value = quyen.LaDieuPhoi;
                    cmd.Parameters.Add("@MaBacSi", SqlDbType.Int).Value =
                        (object)quyen.MaBacSi ?? DBNull.Value;

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            model.Add(new BacSiTrucItem
                            {
                                MaBacSi = Convert.ToInt32(reader["ma_bs"]),
                                TenBacSi = Convert.ToString(reader["TenBacSi"]),
                                TenKhoa = Convert.ToString(reader["TenKhoa"]),
                                SoCho = Convert.ToInt32(reader["SoCho"])
                            });
                        }
                    }
                }
            }
            return View(model);
        }

        // Trang 2: hàng chờ của bác sĩ đã chọn.
        [HttpGet]
        public ActionResult DanhSachCho(int? ma_bs)
        {
            KhongLuuCache();
            using (var conn = new SqlConnection(ChuoiKetNoi))
            {
                conn.Open();
                QuyenNhanVien quyen = LayQuyen(conn);
                if (quyen == null)
                    return new HttpStatusCodeResult(403, "Tài khoản không có quyền nhân viên.");

                // Tương thích trang đăng nhập cũ trỏ tới /BacSi/DanhSachCho.
                if (!ma_bs.HasValue)
                {
                    if (quyen.LaDieuPhoi)
                        return RedirectToAction("ChonBacSiTruc");
                    if (quyen.MaBacSi.HasValue)
                        ma_bs = quyen.MaBacSi.Value;
                    else
                        return new HttpStatusCodeResult(403);
                }

                if (!quyen.DuocXem(ma_bs.Value))
                    return new HttpStatusCodeResult(403, "Không có quyền xem bác sĩ này.");

                var model = new DanhSachChoViewModel
                {
                    MaBacSi = ma_bs.Value,
                    CoQuyenKham = quyen.MaBacSi.HasValue &&
                                  quyen.MaBacSi.Value == ma_bs.Value
                };
                string sqlBacSi = @"
                    SELECT TOP (1) bs.ho_ten AS TenBacSi, ck.ten_chuyen_khoa AS TenKhoa
                    FROM dbo.BAC_SI bs
                    INNER JOIN dbo.CHUYEN_KHOA ck ON ck.ma_ck = bs.ma_ck
                    INNER JOIN dbo.LICH_TRUC lt ON lt.ma_bs = bs.ma_bs
                    INNER JOIN dbo.CA_TRUC ct ON ct.ma_ca = lt.ma_ca
                    WHERE bs.ma_bs = @MaBacSi AND " + DieuKienDangTruc;

                using (var cmd = new SqlCommand(sqlBacSi, conn))
                {
                    cmd.Parameters.Add("@MaBacSi", SqlDbType.Int).Value = ma_bs.Value;
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read())
                        {
                            TempData["LoiChonBacSi"] = "Bác sĩ không còn trong ca trực đang diễn ra.";
                            return RedirectToAction("ChonBacSiTruc");
                        }
                        model.TenBacSi = Convert.ToString(reader["TenBacSi"]);
                        model.TenKhoa = Convert.ToString(reader["TenKhoa"]);
                    }
                }

                string sqlDanhSach = @"
                    SELECT lk.ma_luot, lk.so_thu_tu, bn.ho_ten AS TenBenhNhan,
                           lk.trang_thai, lk.ngay_kham, ct.ten_ca AS TenCa
                    FROM dbo.LUOT_KHAM lk
                    INNER JOIN dbo.BENH_NHAN bn ON bn.ma_bn = lk.ma_bn
                    INNER JOIN dbo.LICH_TRUC lt ON lt.ma_lich_truc = lk.ma_lich_truc
                    INNER JOIN dbo.CA_TRUC ct ON ct.ma_ca = lt.ma_ca
                    WHERE lt.ma_bs = @MaBacSi
                      AND lk.trang_thai IN (N'Chờ khám', N'Đang khám')
                      AND " + DieuKienDangTruc + @"
                    ORDER BY CASE WHEN lk.trang_thai = N'Đang khám' THEN 0 ELSE 1 END,
                             lk.so_thu_tu, lk.ma_luot";

                using (var cmd = new SqlCommand(sqlDanhSach, conn))
                {
                    cmd.Parameters.Add("@MaBacSi", SqlDbType.Int).Value = ma_bs.Value;
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            model.DanhSach.Add(new BenhNhanChoItem
                            {
                                MaLuot = Convert.ToInt32(reader["ma_luot"]),
                                SoThuTu = Convert.ToInt32(reader["so_thu_tu"]),
                                TenBenhNhan = Convert.ToString(reader["TenBenhNhan"]),
                                TrangThai = Convert.ToString(reader["trang_thai"]),
                                NgayKham = Convert.ToDateTime(reader["ngay_kham"]),
                                TenCa = Convert.ToString(reader["TenCa"])
                            });
                        }
                    }
                }
                return View(model);
            }
        }

        // Gọi lượt tiếp theo: POST + kiểm tra quyền + khóa đồng thời + kiểm tra ca.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult GoiSoTiepTheo(int ma_bs)
        {
            if (ma_bs <= 0) return new HttpStatusCodeResult(400);

            try
            {
                using (var conn = new SqlConnection(ChuoiKetNoi))
                {
                    conn.Open();
                    using (var tran = conn.BeginTransaction(IsolationLevel.Serializable))
                    {
                        try
                        {
                            QuyenNhanVien quyen = LayQuyen(conn, tran);
                            if (quyen == null || !quyen.DuocXem(ma_bs))
                            {
                                tran.Rollback();
                                return new HttpStatusCodeResult(403, "Không có quyền gọi số của bác sĩ này.");
                            }

                            const string sqlKhoa = @"
                                DECLARE @KetQua INT;
                                EXEC @KetQua = sys.sp_getapplock
                                    @Resource = @TenKhoa,
                                    @LockMode = 'Exclusive',
                                    @LockOwner = 'Transaction',
                                    @LockTimeout = 10000;
                                SELECT @KetQua;";
                            using (var cmd = new SqlCommand(sqlKhoa, conn, tran))
                            {
                                cmd.Parameters.Add("@TenKhoa", SqlDbType.NVarChar, 255)
                                    .Value = "eHealthCare_GoiSo_BS_" + ma_bs;
                                if (Convert.ToInt32(cmd.ExecuteScalar()) < 0)
                                    throw new InvalidOperationException("Không lấy được khóa gọi số.");
                            }

                            string sqlDangKham = @"
                                SELECT COUNT(1)
                                FROM dbo.LUOT_KHAM lk
                                INNER JOIN dbo.LICH_TRUC lt ON lt.ma_lich_truc = lk.ma_lich_truc
                                INNER JOIN dbo.CA_TRUC ct ON ct.ma_ca = lt.ma_ca
                                WHERE lt.ma_bs = @MaBacSi
                                  AND lk.trang_thai = N'Đang khám'
                                  AND " + DieuKienDangTruc;
                            using (var cmd = new SqlCommand(sqlDangKham, conn, tran))
                            {
                                cmd.Parameters.Add("@MaBacSi", SqlDbType.Int).Value = ma_bs;
                                if (Convert.ToInt32(cmd.ExecuteScalar()) > 0)
                                {
                                    tran.Commit();
                                    TempData["LoiGoiSo"] =
                                        "Còn bệnh nhân đang khám. Cần hoàn tất lượt đó trước khi gọi tiếp.";
                                    return RedirectToAction("DanhSachCho", new { ma_bs = ma_bs });
                                }
                            }

                            string sqlTiepTheo = @"
                                SELECT TOP (1) lk.ma_luot
                                FROM dbo.LUOT_KHAM lk WITH (UPDLOCK, HOLDLOCK)
                                INNER JOIN dbo.LICH_TRUC lt ON lt.ma_lich_truc = lk.ma_lich_truc
                                INNER JOIN dbo.CA_TRUC ct ON ct.ma_ca = lt.ma_ca
                                WHERE lt.ma_bs = @MaBacSi
                                  AND lk.trang_thai = N'Chờ khám'
                                  AND " + DieuKienDangTruc + @"
                                ORDER BY lk.so_thu_tu, lk.ma_luot";
                            int maLuot;
                            using (var cmd = new SqlCommand(sqlTiepTheo, conn, tran))
                            {
                                cmd.Parameters.Add("@MaBacSi", SqlDbType.Int).Value = ma_bs;
                                object result = cmd.ExecuteScalar();
                                if (result == null)
                                {
                                    tran.Commit();
                                    TempData["LoiGoiSo"] =
                                        "Không có bệnh nhân chờ trong ca trực đang diễn ra.";
                                    return RedirectToAction("DanhSachCho", new { ma_bs = ma_bs });
                                }
                                maLuot = Convert.ToInt32(result);
                            }

                            const string sqlCapNhat = @"
                                UPDATE dbo.LUOT_KHAM
                                SET trang_thai = N'Đang khám'
                                WHERE ma_luot = @MaLuot AND trang_thai = N'Chờ khám'";
                            using (var cmd = new SqlCommand(sqlCapNhat, conn, tran))
                            {
                                cmd.Parameters.Add("@MaLuot", SqlDbType.Int).Value = maLuot;
                                if (cmd.ExecuteNonQuery() != 1)
                                    throw new InvalidOperationException("Lượt khám đã thay đổi, vui lòng thử lại.");
                            }

                            tran.Commit();
                            TempData["ThongBao"] = "Đã gọi số tiếp theo. Lượt khám chuyển sang Đang khám.";
                            return RedirectToAction("DanhSachCho", new { ma_bs = ma_bs });
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
                System.Diagnostics.Debug.WriteLine("LOI GOI SO: " + ex);
                TempData["LoiGoiSo"] = "Không gọi được số. Vui lòng thử lại hoặc xem Output trong Visual Studio.";
                return RedirectToAction("DanhSachCho", new { ma_bs = ma_bs });
            }
        }

        // Bác sĩ mở bệnh nhân của chính mình đang trong trạng thái Đang khám.
        // Nhân viên điều phối dù được chọn bác sĩ cũng không có quyền chẩn đoán hộ.
        [HttpGet]
        public ActionResult KhamBenh(int? id)
        {
            KhongLuuCache();
            if (!id.HasValue || id.Value <= 0)
                return new HttpStatusCodeResult(400, "Thiếu mã lượt khám.");

            if (User.Identity == null || !User.Identity.IsAuthenticated)
                return RedirectToAction("Login", "Account");

            const string sql = @"
                SELECT TOP (1)
                       lk.ma_luot, lk.so_thu_tu, lk.ngay_kham,
                       bn.ho_ten AS TenBenhNhan,
                       bs.ma_bs AS MaBacSi, bs.ho_ten AS TenBacSi,
                       ck.ten_chuyen_khoa AS TenKhoa,
                       (SELECT TOP (1) pk.benh_chan_doan
                        FROM dbo.PHIEU_KHAM pk
                        WHERE pk.ma_luot = lk.ma_luot
                        ORDER BY pk.ma_phieu DESC) AS BenhChanDoan
                FROM dbo.LUOT_KHAM lk
                INNER JOIN dbo.BENH_NHAN bn ON bn.ma_bn = lk.ma_bn
                INNER JOIN dbo.LICH_TRUC lt ON lt.ma_lich_truc = lk.ma_lich_truc
                INNER JOIN dbo.BAC_SI bs ON bs.ma_bs = lt.ma_bs
                INNER JOIN dbo.CHUYEN_KHOA ck ON ck.ma_ck = bs.ma_ck
                INNER JOIN dbo.WEB_BAC_SI_TAI_KHOAN bstk ON bstk.MaBacSi = bs.ma_bs
                INNER JOIN dbo.WEB_TAI_KHOAN tk ON tk.MaNguoiDung = bstk.MaNguoiDung
                WHERE lk.ma_luot = @MaLuot
                  AND lk.trang_thai = N'Đang khám'
                  AND tk.TenDangNhap = @TenDangNhap
                  AND tk.MaBenhNhan IS NULL";

            using (var conn = new SqlConnection(ChuoiKetNoi))
            {
                conn.Open();
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.Add("@MaLuot", SqlDbType.Int).Value = id.Value;
                    cmd.Parameters.Add("@TenDangNhap", SqlDbType.NVarChar, 50)
                       .Value = User.Identity.Name;

                    using (var reader = cmd.ExecuteReader())
                    {
                        // Không tiết lộ dữ liệu của bác sĩ khác hoặc lượt không được phép khám.
                        if (!reader.Read())
                            return HttpNotFound("Không có lượt khám phù hợp với tài khoản bác sĩ này.");

                        var model = new KhamBenhViewModel
                        {
                            MaLuot = Convert.ToInt32(reader["ma_luot"]),
                            SoThuTu = Convert.ToInt32(reader["so_thu_tu"]),
                            NgayKham = Convert.ToDateTime(reader["ngay_kham"]),
                            MaBacSi = Convert.ToInt32(reader["MaBacSi"]),
                            TenBenhNhan = Convert.ToString(reader["TenBenhNhan"]),
                            TenBacSi = Convert.ToString(reader["TenBacSi"]),
                            TenKhoa = Convert.ToString(reader["TenKhoa"]),
                            BenhChanDoan = reader["BenhChanDoan"] == DBNull.Value
                                ? "" : Convert.ToString(reader["BenhChanDoan"])
                        };
                        return View(model);
                    }
                }
            }
        }

        // Chỉ lưu chẩn đoán. Chưa chuyển trạng thái Đã khám, để còn kê đơn thuốc.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult LuuChanDoan(int maLuot, string benhChanDoan)
        {
            if (maLuot <= 0) return new HttpStatusCodeResult(400);
            if (User.Identity == null || !User.Identity.IsAuthenticated)
                return RedirectToAction("Login", "Account");

            benhChanDoan = (benhChanDoan ?? "").Trim();
            if (benhChanDoan.Length == 0 || benhChanDoan.Length > 500)
            {
                TempData["LoiKham"] = "Chẩn đoán phải có từ 1 đến 500 ký tự.";
                return RedirectToAction("KhamBenh", new { id = maLuot });
            }

            try
            {
                using (var conn = new SqlConnection(ChuoiKetNoi))
                {
                    conn.Open();
                    using (var tran = conn.BeginTransaction(IsolationLevel.Serializable))
                    {
                        try
                        {
                            // Khóa theo lượt khám để không tạo 2 phiếu khi bấm Lưu đồng thời.
                            const string sqlKhoa = @"
                                DECLARE @KetQua INT;
                                EXEC @KetQua = sys.sp_getapplock
                                    @Resource = @TenKhoa,
                                    @LockMode = 'Exclusive',
                                    @LockOwner = 'Transaction',
                                    @LockTimeout = 10000;
                                SELECT @KetQua;";
                            using (var cmd = new SqlCommand(sqlKhoa, conn, tran))
                            {
                                cmd.Parameters.Add("@TenKhoa", SqlDbType.NVarChar, 255)
                                   .Value = "eHealthCare_PhieuKham_" + maLuot;
                                if (Convert.ToInt32(cmd.ExecuteScalar()) < 0)
                                    throw new InvalidOperationException("Không thể khóa phiếu khám.");
                            }

                            // Kiểm tra lại quyền bác sĩ và trạng thái trong cùng giao dịch.
                            const string sqlQuyen = @"
                                SELECT COUNT(1)
                                FROM dbo.LUOT_KHAM lk WITH (UPDLOCK, HOLDLOCK)
                                INNER JOIN dbo.LICH_TRUC lt ON lt.ma_lich_truc = lk.ma_lich_truc
                                INNER JOIN dbo.WEB_BAC_SI_TAI_KHOAN bstk
                                    ON bstk.MaBacSi = lt.ma_bs
                                INNER JOIN dbo.WEB_TAI_KHOAN tk
                                    ON tk.MaNguoiDung = bstk.MaNguoiDung
                                WHERE lk.ma_luot = @MaLuot
                                  AND lk.trang_thai = N'Đang khám'
                                  AND tk.TenDangNhap = @TenDangNhap
                                  AND tk.MaBenhNhan IS NULL";
                            using (var cmd = new SqlCommand(sqlQuyen, conn, tran))
                            {
                                cmd.Parameters.Add("@MaLuot", SqlDbType.Int).Value = maLuot;
                                cmd.Parameters.Add("@TenDangNhap", SqlDbType.NVarChar, 50)
                                   .Value = User.Identity.Name;
                                if (Convert.ToInt32(cmd.ExecuteScalar()) != 1)
                                {
                                    tran.Rollback();
                                    return new HttpStatusCodeResult(
                                        403, "Không có quyền lưu chẩn đoán cho lượt khám này.");
                                }
                            }

                            // Nếu phiếu đã tồn tại thì cập nhật; nếu chưa có thì tạo mới.
                            int? maPhieu = null;
                            const string sqlTimPhieu = @"
                                SELECT TOP (1) ma_phieu
                                FROM dbo.PHIEU_KHAM WITH (UPDLOCK, HOLDLOCK)
                                WHERE ma_luot = @MaLuot
                                ORDER BY ma_phieu DESC";
                            using (var cmd = new SqlCommand(sqlTimPhieu, conn, tran))
                            {
                                cmd.Parameters.Add("@MaLuot", SqlDbType.Int).Value = maLuot;
                                object value = cmd.ExecuteScalar();
                                if (value != null && value != DBNull.Value)
                                    maPhieu = Convert.ToInt32(value);
                            }

                            if (maPhieu.HasValue)
                            {
                                const string sqlSua = @"
                                    UPDATE dbo.PHIEU_KHAM
                                    SET benh_chan_doan = @BenhChanDoan
                                    WHERE ma_phieu = @MaPhieu";
                                using (var cmd = new SqlCommand(sqlSua, conn, tran))
                                {
                                    cmd.Parameters.Add("@BenhChanDoan", SqlDbType.NVarChar, 500)
                                       .Value = benhChanDoan;
                                    cmd.Parameters.Add("@MaPhieu", SqlDbType.Int).Value = maPhieu.Value;
                                    if (cmd.ExecuteNonQuery() != 1)
                                        throw new InvalidOperationException("Không cập nhật được phiếu khám.");
                                }
                            }
                            else
                            {
                                const string sqlThem = @"
                                    INSERT INTO dbo.PHIEU_KHAM(ma_luot, benh_chan_doan)
                                    VALUES (@MaLuot, @BenhChanDoan)";
                                using (var cmd = new SqlCommand(sqlThem, conn, tran))
                                {
                                    cmd.Parameters.Add("@MaLuot", SqlDbType.Int).Value = maLuot;
                                    cmd.Parameters.Add("@BenhChanDoan", SqlDbType.NVarChar, 500)
                                       .Value = benhChanDoan;
                                    if (cmd.ExecuteNonQuery() != 1)
                                        throw new InvalidOperationException("Không lưu được phiếu khám.");
                                }
                            }

                            tran.Commit();
                            TempData["ThongBaoKham"] = "Đã lưu chẩn đoán vào PHIEU_KHAM.";
                            return RedirectToAction("KhamBenh", new { id = maLuot });
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
                System.Diagnostics.Debug.WriteLine("LOI LUU CHAN DOAN: " + ex);
                TempData["LoiKham"] = "Không thể lưu chẩn đoán. Kiểm tra Output Visual Studio.";
                return RedirectToAction("KhamBenh", new { id = maLuot });
            }
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public ActionResult HoanTatKham(int maLuot, int maBacSi)
        {
            if (maLuot <= 0 || maBacSi <= 0)
                return new HttpStatusCodeResult(400);

            string tenDangNhap = User.Identity.Name;

            if (string.IsNullOrWhiteSpace(tenDangNhap))
                return RedirectToAction("Login", "Account");

            try
            {
                using (var conn = new SqlConnection(ChuoiKetNoi))
                {
                    conn.Open();

                    // Chỉ bác sĩ phụ trách lượt khám mới có quyền hoàn tất.
                    // Bắt buộc đã lưu chẩn đoán trước đó.
                    const string sql = @"
                UPDATE lk
                SET lk.trang_thai = N'Đã khám'
                OUTPUT INSERTED.ma_luot
                FROM dbo.LUOT_KHAM lk
                INNER JOIN dbo.LICH_TRUC lt
                    ON lk.ma_lich_truc = lt.ma_lich_truc
                INNER JOIN dbo.WEB_BAC_SI_TAI_KHOAN bstk
                    ON bstk.MaBacSi = lt.ma_bs
                INNER JOIN dbo.WEB_TAI_KHOAN tk
                    ON tk.MaNguoiDung = bstk.MaNguoiDung
                WHERE lk.ma_luot = @MaLuot
                  AND lt.ma_bs = @MaBacSi
                  AND tk.TenDangNhap = @TenDangNhap
                  AND lk.trang_thai = N'Đang khám'
                  AND EXISTS
                  (
                      SELECT 1
                      FROM dbo.PHIEU_KHAM pk
                      WHERE pk.ma_luot = lk.ma_luot
                        AND NULLIF(
                            LTRIM(RTRIM(pk.benh_chan_doan)),
                            N''
                        ) IS NOT NULL
                  )";

                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.Add(
                            "@MaLuot", SqlDbType.Int
                        ).Value = maLuot;

                        cmd.Parameters.Add(
                            "@MaBacSi", SqlDbType.Int
                        ).Value = maBacSi;

                        cmd.Parameters.Add(
                            "@TenDangNhap", SqlDbType.NVarChar, 50
                        ).Value = tenDangNhap;

                        object result = cmd.ExecuteScalar();

                        if (result == null)
                        {
                            TempData["LoiKham"] =
                                "Không thể hoàn tất lượt khám. " +
                                "Hãy kiểm tra trạng thái, chẩn đoán " +
                                "và quyền của bác sĩ.";

                            return RedirectToAction(
                                "KhamBenh", "BacSi",
                                new { id = maLuot }
                            );
                        }
                    }
                }

                TempData["ThongBao"] =
                    "Đã hoàn tất khám bệnh thành công.";

                return RedirectToAction(
                    "DanhSachCho", "BacSi",
                    new { ma_bs = maBacSi }
                );
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    "LOI HOAN TAT KHAM: " + ex);

                TempData["LoiKham"] =
                    "Không thể hoàn tất khám. " +
                    "Vui lòng kiểm tra lại.";

                return RedirectToAction(
                    "KhamBenh", "BacSi",
                    new { id = maLuot }
                );
            }
        }

    }
}
