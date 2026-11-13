using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;

namespace eHealthCare.Controllers
{
    public class NhaThuocDonItem
    {
        public int MaDon { get; set; }
        public int MaLuot { get; set; }
        public int SoThuTu { get; set; }
        public string TenBenhNhan { get; set; }
        public int SoLoaiThuoc { get; set; }
        public string TrangThaiPhat { get; set; }
    }

    public class NhaThuocThuocItem
    {
        public string TenThuoc { get; set; }
        public int SoLuong { get; set; }
        public string LieuSang { get; set; }
        public string LieuTrua { get; set; }
        public string LieuChieu { get; set; }
        public string LieuToi { get; set; }
        public string TruocHaySauAn { get; set; }
    }

    public class NhaThuocChiTietModel
    {
        public int MaDon { get; set; }
        public int MaLuot { get; set; }
        public int SoThuTu { get; set; }
        public string TenBenhNhan { get; set; }
        public string TenBacSi { get; set; }
        public string BenhChanDoan { get; set; }
        public string TrangThaiKham { get; set; }
        public string TrangThaiPhat { get; set; }
        public string TenNhanVienPhat { get; set; }
        public List<NhaThuocThuocItem> DanhSachThuoc { get; set; }

        public bool CoTheXacNhan
        {
            get
            {
                return TrangThaiKham == "Đã khám" &&
                       TrangThaiPhat == "Chưa phát" &&
                       DanhSachThuoc != null &&
                       DanhSachThuoc.Count > 0;
            }
        }

        public NhaThuocChiTietModel()
        {
            DanhSachThuoc = new List<NhaThuocThuocItem>();
        }
    }

    // QUYEN NHAN VIEN NHA THUOC DUOC XAC DINH TU BANG SQL.
    // KHONG SU DUNG Session HOAC MA NHAN VIEN DO TRINH DUYET GUI LEN DE PHAN QUYEN.
    [Authorize]
    public class NhaThuocController : Controller
    {
        private string ChuoiKetNoi
        {
            get
            {
                return ConfigurationManager.ConnectionStrings["eHealthCareDB"].ConnectionString;
            }
        }

        private void KhongCache()
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

            // Khong cho phep tai khoan benh nhan hay tai khoan bac si dung quyen nha thuoc.
            const string sql = @"
                SELECT nv.MaNhanVien
                FROM dbo.WEB_NHA_THUOC_TAI_KHOAN nv
                INNER JOIN dbo.WEB_TAI_KHOAN tk
                    ON tk.MaNguoiDung = nv.MaNguoiDung
                WHERE tk.TenDangNhap = @TenDangNhap
                  AND tk.MaBenhNhan IS NULL
                  AND NOT EXISTS
                  (
                      SELECT 1 FROM dbo.WEB_BAC_SI_TAI_KHOAN bs
                      WHERE bs.MaNguoiDung = tk.MaNguoiDung
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
        public ActionResult DanhSachDon()
        {
            KhongCache();
            var items = new List<NhaThuocDonItem>();

            try
            {
                using (var conn = new SqlConnection(ChuoiKetNoi))
                {
                    conn.Open();
                    if (!LayMaNhanVien(conn).HasValue)
                        return new HttpStatusCodeResult(403, "Tài khoản không có quyền nhà thuốc.");

                    const string sql = @"
                        SELECT dt.ma_don, lk.ma_luot, lk.so_thu_tu,
                               bn.ho_ten AS TenBenhNhan, dt.trang_thai_phat,
                               (SELECT COUNT(1)
                                FROM dbo.CHI_TIET_DON_THUOC ct
                                WHERE ct.ma_don = dt.ma_don) AS SoLoaiThuoc
                        FROM dbo.DON_THUOC dt
                        INNER JOIN dbo.PHIEU_KHAM pk
                            ON pk.ma_phieu = dt.ma_phieu
                        INNER JOIN dbo.LUOT_KHAM lk
                            ON lk.ma_luot = pk.ma_luot
                        INNER JOIN dbo.BENH_NHAN bn
                            ON bn.ma_bn = lk.ma_bn
                        WHERE lk.trang_thai = N'Đã khám'
                          AND dt.trang_thai_phat = N'Chưa phát'
                        ORDER BY dt.ma_don";

                    using (var cmd = new SqlCommand(sql, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            items.Add(new NhaThuocDonItem
                            {
                                MaDon = Convert.ToInt32(reader["ma_don"]),
                                MaLuot = Convert.ToInt32(reader["ma_luot"]),
                                SoThuTu = Convert.ToInt32(reader["so_thu_tu"]),
                                TenBenhNhan = Convert.ToString(reader["TenBenhNhan"]),
                                SoLoaiThuoc = Convert.ToInt32(reader["SoLoaiThuoc"]),
                                TrangThaiPhat = Convert.ToString(reader["trang_thai_phat"])
                            });
                        }
                    }
                }

                return View(items);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("LOI NHA THUOC DANH SACH: " + ex);
                ViewBag.Error = "Không thể tải danh sách đơn thuốc. Kiểm tra SQL Server và bảng phân quyền.";
                return View(items);
            }
        }

        [HttpGet]
        public ActionResult ChiTietDon(int? id)
        {
            KhongCache();
            if (!id.HasValue || id.Value <= 0)
                return new HttpStatusCodeResult(400, "Thiếu mã đơn thuốc.");

            using (var conn = new SqlConnection(ChuoiKetNoi))
            {
                conn.Open();
                if (!LayMaNhanVien(conn).HasValue)
                    return new HttpStatusCodeResult(403, "Tài khoản không có quyền nhà thuốc.");

                const string sql = @"
                    SELECT dt.ma_don, lk.ma_luot, lk.so_thu_tu,
                           bn.ho_ten AS TenBenhNhan,
                           bs.ho_ten AS TenBacSi,
                           pk.benh_chan_doan,
                           lk.trang_thai AS TrangThaiKham,
                           dt.trang_thai_phat AS TrangThaiPhat,
                           nv.ho_ten AS TenNhanVienPhat
                    FROM dbo.DON_THUOC dt
                    INNER JOIN dbo.PHIEU_KHAM pk
                        ON pk.ma_phieu = dt.ma_phieu
                    INNER JOIN dbo.LUOT_KHAM lk
                        ON lk.ma_luot = pk.ma_luot
                    INNER JOIN dbo.BENH_NHAN bn
                        ON bn.ma_bn = lk.ma_bn
                    INNER JOIN dbo.LICH_TRUC lt
                        ON lt.ma_lich_truc = lk.ma_lich_truc
                    INNER JOIN dbo.BAC_SI bs
                        ON bs.ma_bs = lt.ma_bs
                    LEFT JOIN dbo.NHAN_VIEN_QUAY_THUOC_THU_NGAN nv
                        ON nv.ma_nv = dt.ma_nv_phat
                    WHERE dt.ma_don = @MaDon";

                NhaThuocChiTietModel model;
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.Add("@MaDon", SqlDbType.Int).Value = id.Value;
                    using (var r = cmd.ExecuteReader())
                    {
                        if (!r.Read())
                            return HttpNotFound("Không tìm thấy đơn thuốc.");
                        model = new NhaThuocChiTietModel
                        {
                            MaDon = Convert.ToInt32(r["ma_don"]),
                            MaLuot = Convert.ToInt32(r["ma_luot"]),
                            SoThuTu = Convert.ToInt32(r["so_thu_tu"]),
                            TenBenhNhan = Convert.ToString(r["TenBenhNhan"]),
                            TenBacSi = Convert.ToString(r["TenBacSi"]),
                            BenhChanDoan = Convert.ToString(r["benh_chan_doan"]),
                            TrangThaiKham = Convert.ToString(r["TrangThaiKham"]),
                            TrangThaiPhat = Convert.ToString(r["TrangThaiPhat"]),
                            TenNhanVienPhat = r["TenNhanVienPhat"] == DBNull.Value
                                ? "" : Convert.ToString(r["TenNhanVienPhat"])
                        };
                    }
                }

                const string sqlThuoc = @"
                    SELECT t.ten_thuoc, ct.so_luong, ct.lieu_sang,
                           ct.lieu_trua, ct.lieu_chieu, ct.lieu_toi,
                           ct.truoc_hay_sau_an
                    FROM dbo.CHI_TIET_DON_THUOC ct
                    INNER JOIN dbo.THUOC t ON t.ma_thuoc = ct.ma_thuoc
                    WHERE ct.ma_don = @MaDon
                    ORDER BY t.ten_thuoc";
                using (var cmd = new SqlCommand(sqlThuoc, conn))
                {
                    cmd.Parameters.Add("@MaDon", SqlDbType.Int).Value = id.Value;
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            model.DanhSachThuoc.Add(new NhaThuocThuocItem
                            {
                                TenThuoc = Convert.ToString(r["ten_thuoc"]),
                                SoLuong = Convert.ToInt32(r["so_luong"]),
                                LieuSang = r["lieu_sang"] == DBNull.Value ? "" : Convert.ToString(r["lieu_sang"]),
                                LieuTrua = r["lieu_trua"] == DBNull.Value ? "" : Convert.ToString(r["lieu_trua"]),
                                LieuChieu = r["lieu_chieu"] == DBNull.Value ? "" : Convert.ToString(r["lieu_chieu"]),
                                LieuToi = r["lieu_toi"] == DBNull.Value ? "" : Convert.ToString(r["lieu_toi"]),
                                TruocHaySauAn = Convert.ToString(r["truoc_hay_sau_an"])
                            });
                        }
                    }
                }

                return View(model);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult XacNhanPhat(int maDon, bool? daKiemTra)
        {
            if (maDon <= 0)
                return new HttpStatusCodeResult(400, "Mã đơn thuốc không hợp lệ.");
            if (daKiemTra != true)
            {
                TempData["LoiPhat"] = "Bạn cần xác nhận đã kiểm tra đơn và thuốc trước khi phát.";
                return RedirectToAction("ChiTietDon", new { id = maDon });
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
                            int? maNhanVien = LayMaNhanVien(conn, tran);
                            if (!maNhanVien.HasValue)
                            {
                                tran.Rollback();
                                return new HttpStatusCodeResult(403, "Tài khoản không có quyền nhà thuốc.");
                            }

                            // Cap nhat trang thai va ma nhan vien cung luc,
                            // de thoa CHECK constraint CK_DonThuoc_NVPhat.
                            const string sql = @"
                                UPDATE dt
                                SET dt.trang_thai_phat = N'Đã phát',
                                    dt.ma_nv_phat = @MaNhanVien
                                OUTPUT INSERTED.ma_don
                                FROM dbo.DON_THUOC dt
                                INNER JOIN dbo.PHIEU_KHAM pk
                                    ON pk.ma_phieu = dt.ma_phieu
                                INNER JOIN dbo.LUOT_KHAM lk
                                    ON lk.ma_luot = pk.ma_luot
                                WHERE dt.ma_don = @MaDon
                                  AND dt.trang_thai_phat = N'Chưa phát'
                                  AND dt.ma_nv_phat IS NULL
                                  AND lk.trang_thai = N'Đã khám'
                                  AND EXISTS
                                  (
                                      SELECT 1
                                      FROM dbo.CHI_TIET_DON_THUOC ct
                                      WHERE ct.ma_don = dt.ma_don
                                  )";

                            object maDaPhat;
                            using (var cmd = new SqlCommand(sql, conn, tran))
                            {
                                cmd.Parameters.Add("@MaDon", SqlDbType.Int).Value = maDon;
                                cmd.Parameters.Add("@MaNhanVien", SqlDbType.Int).Value = maNhanVien.Value;
                                maDaPhat = cmd.ExecuteScalar();
                            }

                            if (maDaPhat == null)
                            {
                                tran.Rollback();
                                TempData["LoiPhat"] =
                                    "Đơn không còn đủ điều kiện phát. Hãy tải lại để kiểm tra trạng thái.";
                                return RedirectToAction("ChiTietDon", new { id = maDon });
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

                TempData["ThongBaoPhat"] = "Đã ghi nhận cấp phát thuốc thành công.";
                return RedirectToAction("ChiTietDon", new { id = maDon });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("LOI PHAT THUOC: " + ex);
                TempData["LoiPhat"] =
                    "Không thể xác nhận phát thuốc. Kiểm tra ràng buộc trạng thái và tài khoản nhân viên trong SQL.";
                return RedirectToAction("ChiTietDon", new { id = maDon });
            }
        }
    }
}
