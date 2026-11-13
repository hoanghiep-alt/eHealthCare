using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Web.Mvc;

namespace eHealthCare.Controllers
{
    public class ThuocTrongDonItem
    {
        public int MaThuoc { get; set; }
        public string TenThuoc { get; set; }
        public int SoLuong { get; set; }
        public decimal DonGia { get; set; }
        public string LieuSang { get; set; }
        public string LieuTrua { get; set; }
        public string LieuChieu { get; set; }
        public string LieuToi { get; set; }
        public string TruocHaySauAn { get; set; }
    }

    public class KeDonViewModel
    {
        public int MaLuot { get; set; }
        public int MaPhieu { get; set; }
        public int? MaDon { get; set; }
        public int SoThuTu { get; set; }
        public string TenBenhNhan { get; set; }
        public string TenBacSi { get; set; }
        public string ChanDoan { get; set; }
        public string TrangThaiPhat { get; set; }
        public bool ChoPhepThemThuoc { get; set; }
        public List<SelectListItem> DanhSachThuoc { get; set; }
        public List<ThuocTrongDonItem> ChiTiet { get; set; }

        public KeDonViewModel()
        {
            DanhSachThuoc = new List<SelectListItem>();
            ChiTiet = new List<ThuocTrongDonItem>();
        }
    }

    // Tai khoan dieu phoi KHONG duoc ke don thay bac si.
    // Bac si co quyen la tai khoan dang nhap lien ket voi bs.ma_bs cua luot kham.
    [Authorize]
    public class DonThuocController : Controller
    {
        private string ChuoiKetNoi
        {
            get
            {
                return ConfigurationManager.ConnectionStrings["eHealthCareDB"].ConnectionString;
            }
        }

        // KIEM TRA SQL/01_KiemTraDonThuoc.sql truoc khi su dung.
        // Neu CHECK trang_thai_phat cua DB khac, sua dung gia tri duoc phep tai day.
        private const string TrangThaiDonMoi = "Chưa phát";

        private void KhongLuuCache()
        {
            Response.Cache.SetCacheability(System.Web.HttpCacheability.NoCache);
            Response.Cache.SetNoStore();
            Response.Cache.SetExpires(DateTime.UtcNow.AddMinutes(-1));
        }

        // Chi tra ve phieu khi tai khoan dang nhap la chinh bac si phu trach.
        // Dieu kien trang thai 'Dang kham' co o ca GET va POST.
        private KeDonViewModel LayPhieuDuocPhep(SqlConnection conn, int maLuot,
                                                 SqlTransaction tran = null)
        {
            const string sql = @"
                SELECT TOP (1)
                    pk.ma_phieu, lk.ma_luot, lk.so_thu_tu,
                    bn.ho_ten AS TenBenhNhan,
                    bs.ho_ten AS TenBacSi,
                    pk.benh_chan_doan
                FROM dbo.PHIEU_KHAM pk
                INNER JOIN dbo.LUOT_KHAM lk ON lk.ma_luot = pk.ma_luot
                INNER JOIN dbo.BENH_NHAN bn ON bn.ma_bn = lk.ma_bn
                INNER JOIN dbo.LICH_TRUC lt ON lt.ma_lich_truc = lk.ma_lich_truc
                INNER JOIN dbo.BAC_SI bs ON bs.ma_bs = lt.ma_bs
                INNER JOIN dbo.WEB_BAC_SI_TAI_KHOAN bt ON bt.MaBacSi = bs.ma_bs
                INNER JOIN dbo.WEB_TAI_KHOAN tk ON tk.MaNguoiDung = bt.MaNguoiDung
                WHERE lk.ma_luot = @MaLuot
                  AND lk.trang_thai = N'Đang khám'
                  AND tk.TenDangNhap = @TenDangNhap
                  AND tk.MaBenhNhan IS NULL
                ORDER BY pk.ma_phieu DESC";

            using (var cmd = new SqlCommand(sql, conn, tran))
            {
                cmd.Parameters.Add("@MaLuot", SqlDbType.Int).Value = maLuot;
                cmd.Parameters.Add("@TenDangNhap", SqlDbType.NVarChar, 50)
                   .Value = User.Identity.Name;

                using (var reader = cmd.ExecuteReader())
                {
                    if (!reader.Read()) return null;

                    return new KeDonViewModel
                    {
                        MaLuot = Convert.ToInt32(reader["ma_luot"]),
                        MaPhieu = Convert.ToInt32(reader["ma_phieu"]),
                        SoThuTu = Convert.ToInt32(reader["so_thu_tu"]),
                        TenBenhNhan = Convert.ToString(reader["TenBenhNhan"]),
                        TenBacSi = Convert.ToString(reader["TenBacSi"]),
                        ChanDoan = Convert.ToString(reader["benh_chan_doan"])
                    };
                }
            }
        }

        [HttpGet]
        public ActionResult KeDon(int? id)
        {
            KhongLuuCache();
            if (!id.HasValue || id.Value <= 0)
                return new HttpStatusCodeResult(400, "Thiếu mã lượt khám.");

            using (var conn = new SqlConnection(ChuoiKetNoi))
            {
                conn.Open();
                KeDonViewModel model = LayPhieuDuocPhep(conn, id.Value);
                if (model == null)
                    return HttpNotFound("Không có phiếu khám được phép kê đơn.");

                const string sqlThuoc = @"
                    SELECT ma_thuoc, ten_thuoc, don_gia
                    FROM dbo.THUOC ORDER BY ten_thuoc, ma_thuoc";
                using (var cmd = new SqlCommand(sqlThuoc, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int maThuoc = Convert.ToInt32(reader["ma_thuoc"]);
                        string tenThuoc = Convert.ToString(reader["ten_thuoc"]);
                        decimal donGia = Convert.ToDecimal(reader["don_gia"]);
                        model.DanhSachThuoc.Add(new SelectListItem
                        {
                            Value = maThuoc.ToString(),
                            Text = tenThuoc + " (" + donGia.ToString("N0") + " đ)"
                        });
                    }
                }

                const string sqlDon = @"
                    SELECT TOP (1) ma_don, trang_thai_phat
                    FROM dbo.DON_THUOC
                    WHERE ma_phieu = @MaPhieu
                    ORDER BY ma_don DESC";
                using (var cmd = new SqlCommand(sqlDon, conn))
                {
                    cmd.Parameters.Add("@MaPhieu", SqlDbType.Int).Value = model.MaPhieu;
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            model.MaDon = Convert.ToInt32(reader["ma_don"]);
                            model.TrangThaiPhat = Convert.ToString(reader["trang_thai_phat"]);
                        }
                    }
                }

                if (model.MaDon.HasValue)
                {
                    const string sqlChiTiet = @"
                        SELECT ct.ma_thuoc, t.ten_thuoc, t.don_gia,
                               ct.so_luong, ct.lieu_sang, ct.lieu_trua,
                               ct.lieu_chieu, ct.lieu_toi, ct.truoc_hay_sau_an
                        FROM dbo.CHI_TIET_DON_THUOC ct
                        INNER JOIN dbo.THUOC t ON t.ma_thuoc = ct.ma_thuoc
                        WHERE ct.ma_don = @MaDon
                        ORDER BY t.ten_thuoc";
                    using (var cmd = new SqlCommand(sqlChiTiet, conn))
                    {
                        cmd.Parameters.Add("@MaDon", SqlDbType.Int).Value = model.MaDon.Value;
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                model.ChiTiet.Add(new ThuocTrongDonItem
                                {
                                    MaThuoc = Convert.ToInt32(reader["ma_thuoc"]),
                                    TenThuoc = Convert.ToString(reader["ten_thuoc"]),
                                    DonGia = Convert.ToDecimal(reader["don_gia"]),
                                    SoLuong = Convert.ToInt32(reader["so_luong"]),
                                    LieuSang = reader["lieu_sang"] == DBNull.Value ? "" : Convert.ToString(reader["lieu_sang"]),
                                    LieuTrua = reader["lieu_trua"] == DBNull.Value ? "" : Convert.ToString(reader["lieu_trua"]),
                                    LieuChieu = reader["lieu_chieu"] == DBNull.Value ? "" : Convert.ToString(reader["lieu_chieu"]),
                                    LieuToi = reader["lieu_toi"] == DBNull.Value ? "" : Convert.ToString(reader["lieu_toi"]),
                                    TruocHaySauAn = Convert.ToString(reader["truoc_hay_sau_an"])
                                });
                            }
                        }
                    }
                }
                model.ChoPhepThemThuoc = model.DanhSachThuoc.Count > 0 &&
                    (!model.MaDon.HasValue || model.TrangThaiPhat == TrangThaiDonMoi);
                return View(model);
            }
        }

        private static string LieuHopLe(string lieu)
        {
            lieu = (lieu ?? "").Trim();
            return lieu.Length <= 50 ? lieu : null;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ThemThuoc(int maLuot, int maThuoc, int soLuong,
                                     string lieuSang, string lieuTrua,
                                     string lieuChieu, string lieuToi,
                                     string truocHaySauAn)
        {
            if (maLuot <= 0 || maThuoc <= 0 || soLuong < 1 || soLuong > 1000)
                return new HttpStatusCodeResult(400, "Thông tin thuốc không hợp lệ.");

            lieuSang = LieuHopLe(lieuSang);
            lieuTrua = LieuHopLe(lieuTrua);
            lieuChieu = LieuHopLe(lieuChieu);
            lieuToi = LieuHopLe(lieuToi);
            if (lieuSang == null || lieuTrua == null || lieuChieu == null || lieuToi == null)
            {
                TempData["LoiKeDon"] = "Mỗi ô liều dùng không được vượt quá 50 ký tự.";
                return RedirectToAction("KeDon", new { id = maLuot });
            }
            if (truocHaySauAn != "Trước ăn" && truocHaySauAn != "Sau ăn")
            {
                TempData["LoiKeDon"] = "Vui lòng chọn trước ăn hoặc sau ăn.";
                return RedirectToAction("KeDon", new { id = maLuot });
            }
            if (string.IsNullOrWhiteSpace(lieuSang) &&
                string.IsNullOrWhiteSpace(lieuTrua) &&
                string.IsNullOrWhiteSpace(lieuChieu) &&
                string.IsNullOrWhiteSpace(lieuToi))
            {
                TempData["LoiKeDon"] = "Bác sĩ cần nhập ít nhất một hướng dẫn liều dùng.";
                return RedirectToAction("KeDon", new { id = maLuot });
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
                            KeDonViewModel phieu = LayPhieuDuocPhep(conn, maLuot, tran);
                            if (phieu == null)
                            {
                                tran.Rollback();
                                return new HttpStatusCodeResult(403,
                                    "Không có quyền kê đơn cho lượt khám này.");
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
                                    .Value = "eHealthCare_DonThuoc_" + phieu.MaPhieu;
                                if (Convert.ToInt32(cmd.ExecuteScalar()) < 0)
                                    throw new InvalidOperationException("Không lấy được khóa đơn thuốc.");
                            }

                            // Lock app theo phieu kham; re-check trang thai sau khi lock.
                            phieu = LayPhieuDuocPhep(conn, maLuot, tran);
                            if (phieu == null)
                            {
                                tran.Rollback();
                                return new HttpStatusCodeResult(403, "Lượt khám không còn hợp lệ.");
                            }

                            using (var cmd = new SqlCommand(
                                "SELECT COUNT(1) FROM dbo.THUOC WHERE ma_thuoc = @MaThuoc",
                                conn, tran))
                            {
                                cmd.Parameters.Add("@MaThuoc", SqlDbType.Int).Value = maThuoc;
                                if (Convert.ToInt32(cmd.ExecuteScalar()) != 1)
                                {
                                    tran.Commit();
                                    TempData["LoiKeDon"] = "Thuốc không tồn tại trong danh mục.";
                                    return RedirectToAction("KeDon", new { id = maLuot });
                                }
                            }

                            int maDon = 0;
                            string trangThaiPhat = null;
                            const string sqlTimDon = @"
                                SELECT TOP (1) ma_don, trang_thai_phat
                                FROM dbo.DON_THUOC WITH (UPDLOCK, HOLDLOCK)
                                WHERE ma_phieu = @MaPhieu
                                ORDER BY ma_don DESC";
                            using (var cmd = new SqlCommand(sqlTimDon, conn, tran))
                            {
                                cmd.Parameters.Add("@MaPhieu", SqlDbType.Int).Value = phieu.MaPhieu;
                                using (var reader = cmd.ExecuteReader())
                                {
                                    if (reader.Read())
                                    {
                                        maDon = Convert.ToInt32(reader["ma_don"]);
                                        trangThaiPhat = Convert.ToString(reader["trang_thai_phat"]);
                                    }
                                }
                            }

                            if (maDon != 0 && trangThaiPhat != TrangThaiDonMoi)
                            {
                                tran.Commit();
                                TempData["LoiKeDon"] = "Đơn thuốc đã chuyển trạng thái phát; không thể sửa.";
                                return RedirectToAction("KeDon", new { id = maLuot });
                            }

                            if (maDon != 0)
                            {
                                const string sqlTrung = @"
                                    SELECT COUNT(1)
                                    FROM dbo.CHI_TIET_DON_THUOC
                                    WHERE ma_don = @MaDon AND ma_thuoc = @MaThuoc";
                                using (var cmd = new SqlCommand(sqlTrung, conn, tran))
                                {
                                    cmd.Parameters.Add("@MaDon", SqlDbType.Int).Value = maDon;
                                    cmd.Parameters.Add("@MaThuoc", SqlDbType.Int).Value = maThuoc;
                                    if (Convert.ToInt32(cmd.ExecuteScalar()) > 0)
                                    {
                                        tran.Commit();
                                        TempData["LoiKeDon"] = "Thuốc đã có trong đơn. Không thêm trùng.";
                                        return RedirectToAction("KeDon", new { id = maLuot });
                                    }
                                }
                            }
                            else
                            {
                                const string sqlTaoDon = @"
                                    INSERT INTO dbo.DON_THUOC
                                        (ma_phieu, ma_nv_phat, trang_thai_phat)
                                    OUTPUT INSERTED.ma_don
                                    VALUES (@MaPhieu, NULL, @TrangThai)";
                                using (var cmd = new SqlCommand(sqlTaoDon, conn, tran))
                                {
                                    cmd.Parameters.Add("@MaPhieu", SqlDbType.Int).Value = phieu.MaPhieu;
                                    cmd.Parameters.Add("@TrangThai", SqlDbType.NVarChar, 20)
                                        .Value = TrangThaiDonMoi;
                                    maDon = Convert.ToInt32(cmd.ExecuteScalar());
                                }
                            }

                            const string sqlThem = @"
                                INSERT INTO dbo.CHI_TIET_DON_THUOC
                                  (ma_don, ma_thuoc, so_luong,
                                   lieu_sang, lieu_trua, lieu_chieu, lieu_toi, truoc_hay_sau_an)
                                VALUES
                                  (@MaDon, @MaThuoc, @SoLuong,
                                   @Sang, @Trua, @Chieu, @Toi, @An)";
                            using (var cmd = new SqlCommand(sqlThem, conn, tran))
                            {
                                cmd.Parameters.Add("@MaDon", SqlDbType.Int).Value = maDon;
                                cmd.Parameters.Add("@MaThuoc", SqlDbType.Int).Value = maThuoc;
                                cmd.Parameters.Add("@SoLuong", SqlDbType.Int).Value = soLuong;
                                cmd.Parameters.Add("@Sang", SqlDbType.NVarChar, 50).Value =
                                    string.IsNullOrWhiteSpace(lieuSang) ? (object)DBNull.Value : lieuSang;
                                cmd.Parameters.Add("@Trua", SqlDbType.NVarChar, 50).Value =
                                    string.IsNullOrWhiteSpace(lieuTrua) ? (object)DBNull.Value : lieuTrua;
                                cmd.Parameters.Add("@Chieu", SqlDbType.NVarChar, 50).Value =
                                    string.IsNullOrWhiteSpace(lieuChieu) ? (object)DBNull.Value : lieuChieu;
                                cmd.Parameters.Add("@Toi", SqlDbType.NVarChar, 50).Value =
                                    string.IsNullOrWhiteSpace(lieuToi) ? (object)DBNull.Value : lieuToi;
                                cmd.Parameters.Add("@An", SqlDbType.NVarChar, 10).Value = truocHaySauAn;
                                cmd.ExecuteNonQuery();
                            }

                            tran.Commit();
                            TempData["ThongBaoKeDon"] = "Đã thêm thuốc vào đơn thành công.";
                            return RedirectToAction("KeDon", new { id = maLuot });
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
                System.Diagnostics.Debug.WriteLine("LOI KE DON: " + ex);
                TempData["LoiKeDon"] = Request.IsLocal
                    ? "Không lưu được đơn thuốc: " + ex.Message
                    : "Chưa thể lưu đơn thuốc. Vui lòng liên hệ nhân viên phụ trách.";
                return RedirectToAction("KeDon", new { id = maLuot });
            }
        }
    }
}
