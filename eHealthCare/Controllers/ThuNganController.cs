using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;

namespace eHealthCare.Controllers
{
    public class ThuNganDanhSachItem
    {
        public int MaLuot { get; set; }
        public int SoThuTu { get; set; }
        public string TenBenhNhan { get; set; }
        public DateTime NgayKham { get; set; }
        public int SoDon { get; set; }
        public int DonChuaPhat { get; set; }
        public bool DaThanhToan { get; set; }
    }

    public class ThuNganThuocItem
    {
        public string TenThuoc { get; set; }
        public int SoLuong { get; set; }
        public decimal DonGia { get; set; }
        public decimal ThanhTien { get; set; }
    }

    public class ThuNganChiTietModel
    {
        public int MaLuot { get; set; }
        public int SoThuTu { get; set; }
        public string TenBenhNhan { get; set; }
        public string TenKhoa { get; set; }
        public DateTime NgayKham { get; set; }
        public decimal TienKham { get; set; }
        public decimal TienThuoc { get; set; }
        public decimal TongTien { get { return TienKham + TienThuoc; } }
        public int SoDon { get; set; }
        public int DonChuaPhat { get; set; }
        public int? MaThanhToan { get; set; }
        public DateTime? NgayThanhToan { get; set; }
        public string TenThuNgan { get; set; }
        public List<ThuNganThuocItem> Thuoc { get; set; }

        public bool CoTheThanhToan
        {
            get { return !MaThanhToan.HasValue && DonChuaPhat == 0; }
        }

        public ThuNganChiTietModel()
        {
            Thuoc = new List<ThuNganThuocItem>();
        }
    }

    // CHI TAI KHOAN DUOC LIEN KET QUYEN THU NGAN MOI DUOC THU TIEN.
    // Ban demo nay chi ho tro THU TIEN MAT, KHONG TU TINH MUC HUONG BHYT.
    [Authorize]
    public class ThuNganController : Controller
    {
        private string ChuoiKetNoi
        {
            get
            {
                return ConfigurationManager
                    .ConnectionStrings["eHealthCareDB"].ConnectionString;
            }
        }

        private void KhongLuuCache()
        {
            Response.Cache.SetCacheability(System.Web.HttpCacheability.NoCache);
            Response.Cache.SetNoStore();
            Response.Cache.SetExpires(DateTime.UtcNow.AddMinutes(-1));
        }

        private int? LayMaNhanVien(SqlConnection conn, SqlTransaction tran = null)
        {
            if (!User.Identity.IsAuthenticated ||
                string.IsNullOrWhiteSpace(User.Identity.Name))
                return null;

            const string sql = @"
                SELECT q.MaNhanVien
                FROM dbo.WEB_THU_NGAN_TAI_KHOAN q
                JOIN dbo.WEB_TAI_KHOAN tk ON tk.MaNguoiDung = q.MaNguoiDung
                WHERE tk.TenDangNhap = @TenDangNhap
                  AND tk.MaBenhNhan IS NULL
                  AND NOT EXISTS
                  (
                      SELECT 1 FROM dbo.WEB_BAC_SI_TAI_KHOAN b
                      WHERE b.MaNguoiDung = tk.MaNguoiDung
                  )
                  AND NOT EXISTS
                  (
                      SELECT 1 FROM dbo.WEB_NHA_THUOC_TAI_KHOAN n
                      WHERE n.MaNguoiDung = tk.MaNguoiDung
                  )";

            using (var cmd = new SqlCommand(sql, conn, tran))
            {
                cmd.Parameters.Add("@TenDangNhap", SqlDbType.NVarChar, 50)
                    .Value = User.Identity.Name;
                object result = cmd.ExecuteScalar();
                return result == null || result == DBNull.Value
                    ? (int?)null : Convert.ToInt32(result);
            }
        }

        [HttpGet]
        public ActionResult DanhSach()
        {
            KhongLuuCache();
            var danhSach = new List<ThuNganDanhSachItem>();
            try
            {
                using (var conn = new SqlConnection(ChuoiKetNoi))
                {
                    conn.Open();
                    if (!LayMaNhanVien(conn).HasValue)
                        return new HttpStatusCodeResult(403, "Không có quyền thu ngân.");

                    const string sql = @"
                        SELECT lk.ma_luot, lk.so_thu_tu, lk.ngay_kham,
                               bn.ho_ten AS TenBenhNhan,
                               (SELECT COUNT(*)
                                FROM dbo.PHIEU_KHAM pk
                                JOIN dbo.DON_THUOC dt ON dt.ma_phieu = pk.ma_phieu
                                WHERE pk.ma_luot = lk.ma_luot) AS SoDon,
                               (SELECT COUNT(*)
                                FROM dbo.PHIEU_KHAM pk
                                JOIN dbo.DON_THUOC dt ON dt.ma_phieu = pk.ma_phieu
                                WHERE pk.ma_luot = lk.ma_luot
                                  AND (dt.trang_thai_phat <> N'Đã phát'
                                       OR NOT EXISTS
                                       (SELECT 1 FROM dbo.CHI_TIET_DON_THUOC ct
                                        WHERE ct.ma_don = dt.ma_don))) AS DonChuaPhat,
                               CASE WHEN tt.MaThanhToan IS NULL THEN 0 ELSE 1 END AS DaThanhToan
                        FROM dbo.LUOT_KHAM lk
                        JOIN dbo.BENH_NHAN bn ON bn.ma_bn = lk.ma_bn
                        LEFT JOIN dbo.WEB_THANH_TOAN tt ON tt.MaLuot = lk.ma_luot
                        WHERE lk.trang_thai = N'Đã khám'
                        ORDER BY lk.ngay_kham DESC, lk.ma_luot DESC";

                    using (var cmd = new SqlCommand(sql, conn))
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            danhSach.Add(new ThuNganDanhSachItem
                            {
                                MaLuot = Convert.ToInt32(r["ma_luot"]),
                                SoThuTu = Convert.ToInt32(r["so_thu_tu"]),
                                NgayKham = Convert.ToDateTime(r["ngay_kham"]),
                                TenBenhNhan = Convert.ToString(r["TenBenhNhan"]),
                                SoDon = Convert.ToInt32(r["SoDon"]),
                                DonChuaPhat = Convert.ToInt32(r["DonChuaPhat"]),
                                DaThanhToan = Convert.ToInt32(r["DaThanhToan"]) == 1
                            });
                        }
                    }
                }
                return View(danhSach);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("LOI DANH SACH THU NGAN: " + ex);
                ViewBag.Error = "Không tải được danh sách thanh toán. Kiểm tra kết nối SQL và quyền nhân viên.";
                return View(danhSach);
            }
        }

        // Du lieu TinhTien duoc doc lai tren server khi POST.
        private ThuNganChiTietModel LayChiTiet(SqlConnection conn, int maLuot,
                                                 SqlTransaction tran = null)
        {
            const string sql = @"
                SELECT lk.ma_luot, lk.so_thu_tu, lk.ngay_kham,
                       bn.ho_ten AS TenBenhNhan,
                       ck.ten_chuyen_khoa AS TenKhoa,
                       COALESCE(tt.TienKham, CAST(ck.tien_kham AS DECIMAL(18,2))) AS TienKham,
                       COALESCE(tt.TienThuoc,
                           (SELECT SUM(CAST(ct.so_luong AS DECIMAL(18,2)) *
                                       CAST(t.don_gia AS DECIMAL(18,2)))
                            FROM dbo.PHIEU_KHAM pk
                            JOIN dbo.DON_THUOC dt ON dt.ma_phieu = pk.ma_phieu
                            JOIN dbo.CHI_TIET_DON_THUOC ct ON ct.ma_don = dt.ma_don
                            JOIN dbo.THUOC t ON t.ma_thuoc = ct.ma_thuoc
                            WHERE pk.ma_luot = lk.ma_luot), 0) AS TienThuoc,
                       (SELECT COUNT(*)
                        FROM dbo.PHIEU_KHAM pk
                        JOIN dbo.DON_THUOC dt ON dt.ma_phieu = pk.ma_phieu
                        WHERE pk.ma_luot = lk.ma_luot) AS SoDon,
                       (SELECT COUNT(*)
                        FROM dbo.PHIEU_KHAM pk
                        JOIN dbo.DON_THUOC dt ON dt.ma_phieu = pk.ma_phieu
                        WHERE pk.ma_luot = lk.ma_luot
                          AND (dt.trang_thai_phat <> N'Đã phát'
                               OR NOT EXISTS
                               (SELECT 1 FROM dbo.CHI_TIET_DON_THUOC ct
                                WHERE ct.ma_don = dt.ma_don))) AS DonChuaPhat,
                       tt.MaThanhToan, tt.NgayThanhToan,
                       nv.ho_ten AS TenThuNgan
                FROM dbo.LUOT_KHAM lk
                JOIN dbo.BENH_NHAN bn ON bn.ma_bn = lk.ma_bn
                JOIN dbo.LICH_TRUC lt ON lt.ma_lich_truc = lk.ma_lich_truc
                JOIN dbo.BAC_SI bs ON bs.ma_bs = lt.ma_bs
                JOIN dbo.CHUYEN_KHOA ck ON ck.ma_ck = bs.ma_ck
                LEFT JOIN dbo.WEB_THANH_TOAN tt ON tt.MaLuot = lk.ma_luot
                LEFT JOIN dbo.NHAN_VIEN_QUAY_THUOC_THU_NGAN nv
                  ON nv.ma_nv = tt.MaNhanVien
                WHERE lk.ma_luot = @MaLuot
                  AND lk.trang_thai = N'Đã khám'";

            ThuNganChiTietModel model;
            using (var cmd = new SqlCommand(sql, conn, tran))
            {
                cmd.Parameters.Add("@MaLuot", SqlDbType.Int).Value = maLuot;
                using (var r = cmd.ExecuteReader())
                {
                    if (!r.Read()) return null;
                    model = new ThuNganChiTietModel
                    {
                        MaLuot = Convert.ToInt32(r["ma_luot"]),
                        SoThuTu = Convert.ToInt32(r["so_thu_tu"]),
                        NgayKham = Convert.ToDateTime(r["ngay_kham"]),
                        TenBenhNhan = Convert.ToString(r["TenBenhNhan"]),
                        TenKhoa = Convert.ToString(r["TenKhoa"]),
                        TienKham = Convert.ToDecimal(r["TienKham"]),
                        TienThuoc = Convert.ToDecimal(r["TienThuoc"]),
                        SoDon = Convert.ToInt32(r["SoDon"]),
                        DonChuaPhat = Convert.ToInt32(r["DonChuaPhat"]),
                        MaThanhToan = r["MaThanhToan"] == DBNull.Value
                            ? (int?)null : Convert.ToInt32(r["MaThanhToan"]),
                        NgayThanhToan = r["NgayThanhToan"] == DBNull.Value
                            ? (DateTime?)null : Convert.ToDateTime(r["NgayThanhToan"]),
                        TenThuNgan = r["TenThuNgan"] == DBNull.Value
                            ? "" : Convert.ToString(r["TenThuNgan"])
                    };
                }
            }

            const string sqlThuoc = @"
                SELECT t.ten_thuoc, ct.so_luong, t.don_gia,
                       CAST(ct.so_luong AS DECIMAL(18,2)) *
                       CAST(t.don_gia AS DECIMAL(18,2)) AS ThanhTien
                FROM dbo.PHIEU_KHAM pk
                JOIN dbo.DON_THUOC dt ON dt.ma_phieu = pk.ma_phieu
                JOIN dbo.CHI_TIET_DON_THUOC ct ON ct.ma_don = dt.ma_don
                JOIN dbo.THUOC t ON t.ma_thuoc = ct.ma_thuoc
                WHERE pk.ma_luot = @MaLuot
                ORDER BY t.ten_thuoc";

            using (var cmd = new SqlCommand(sqlThuoc, conn, tran))
            {
                cmd.Parameters.Add("@MaLuot", SqlDbType.Int).Value = maLuot;
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        model.Thuoc.Add(new ThuNganThuocItem
                        {
                            TenThuoc = Convert.ToString(r["ten_thuoc"]),
                            SoLuong = Convert.ToInt32(r["so_luong"]),
                            DonGia = Convert.ToDecimal(r["don_gia"]),
                            ThanhTien = Convert.ToDecimal(r["ThanhTien"])
                        });
                    }
                }
            }
            return model;
        }

        [HttpGet]
        public ActionResult ChiTiet(int? id)
        {
            KhongLuuCache();
            if (!id.HasValue || id.Value <= 0)
                return new HttpStatusCodeResult(400, "Thiếu mã lượt khám.");

            using (var conn = new SqlConnection(ChuoiKetNoi))
            {
                conn.Open();
                if (!LayMaNhanVien(conn).HasValue)
                    return new HttpStatusCodeResult(403, "Không có quyền thu ngân.");

                var model = LayChiTiet(conn, id.Value);
                if (model == null)
                    return HttpNotFound("Không tìm thấy lượt khám đã hoàn tất.");
                return View(model);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult XacNhan(int maLuot)
        {
            if (maLuot <= 0)
                return new HttpStatusCodeResult(400, "Mã lượt khám không hợp lệ.");

            try
            {
                using (var conn = new SqlConnection(ChuoiKetNoi))
                {
                    conn.Open();
                    using (var tran = conn.BeginTransaction(IsolationLevel.Serializable))
                    {
                        try
                        {
                            int? maNhanVien = LayMaNhanVien(conn, tran);
                            if (!maNhanVien.HasValue)
                            {
                                tran.Rollback();
                                return new HttpStatusCodeResult(403, "Không có quyền thu ngân.");
                            }

                            // Mot thoi diem chi mot yeu cau thu tien cho cung luot.
                            using (var cmd = new SqlCommand(@"
                                DECLARE @KetQua INT;
                                EXEC @KetQua = sys.sp_getapplock
                                    @Resource = @Khoa,
                                    @LockMode = 'Exclusive',
                                    @LockOwner = 'Transaction',
                                    @LockTimeout = 10000;
                                SELECT @KetQua;", conn, tran))
                            {
                                cmd.Parameters.Add("@Khoa", SqlDbType.NVarChar, 255)
                                   .Value = "eHealthCare_ThanhToan_" + maLuot;
                                if (Convert.ToInt32(cmd.ExecuteScalar()) < 0)
                                    throw new InvalidOperationException("Không lấy được khóa thanh toán.");
                            }

                            var model = LayChiTiet(conn, maLuot, tran);
                            if (model == null)
                            {
                                tran.Rollback();
                                return HttpNotFound("Lượt khám chưa hoàn tất hoặc không tồn tại.");
                            }

                            if (model.MaThanhToan.HasValue)
                            {
                                tran.Commit();
                                TempData["ThongBaoThanhToan"] = "Lượt khám này đã được thanh toán trước đó; không thu hai lần.";
                                return RedirectToAction("ChiTiet", new { id = maLuot });
                            }

                            if (model.DonChuaPhat > 0)
                            {
                                tran.Commit();
                                TempData["LoiThanhToan"] = "Còn đơn thuốc chưa phát hoặc đơn chưa có thuốc. Hãy hoàn tất bên nhà thuốc trước.";
                                return RedirectToAction("ChiTiet", new { id = maLuot });
                            }

                            if (model.TienKham < 0 || model.TienThuoc < 0 || model.TongTien <= 0)
                            {
                                tran.Rollback();
                                TempData["LoiThanhToan"] = "Số tiền không hợp lệ, chưa xác nhận thu.";
                                return RedirectToAction("ChiTiet", new { id = maLuot });
                            }

                            const string sql = @"
                                INSERT INTO dbo.WEB_THANH_TOAN
                                (MaLuot, MaNhanVien, TienKham, TienThuoc, PhuongThuc, TrangThai)
                                OUTPUT INSERTED.MaThanhToan
                                VALUES (@MaLuot, @MaNhanVien, @TienKham, @TienThuoc,
                                        N'Tiền mặt', N'Đã thanh toán')";
                            using (var cmd = new SqlCommand(sql, conn, tran))
                            {
                                cmd.Parameters.Add("@MaLuot", SqlDbType.Int).Value = maLuot;
                                cmd.Parameters.Add("@MaNhanVien", SqlDbType.Int).Value = maNhanVien.Value;
                                var tienKham = cmd.Parameters.Add("@TienKham", SqlDbType.Decimal);
                                tienKham.Precision = 18;
                                tienKham.Scale = 2;
                                tienKham.Value = model.TienKham;
                                var tienThuoc = cmd.Parameters.Add("@TienThuoc", SqlDbType.Decimal);
                                tienThuoc.Precision = 18;
                                tienThuoc.Scale = 2;
                                tienThuoc.Value = model.TienThuoc;
                                cmd.ExecuteScalar();
                            }
                            tran.Commit();
                        }
                        catch
                        {
                            if (tran.Connection != null) tran.Rollback();
                            throw;
                        }
                    }
                }

                TempData["ThongBaoThanhToan"] = "Đã ghi nhận thu tiền mặt cho lượt khám thành công.";
                return RedirectToAction("ChiTiet", new { id = maLuot });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("LOI THANH TOAN: " + ex);
                TempData["LoiThanhToan"] = "Chưa thể ghi nhận thanh toán. Kiểm tra dữ liệu và kết nối SQL Server.";
                return RedirectToAction("ChiTiet", new { id = maLuot });
            }
        }
    }
}
