
-- 2026-08-21T02:23:51.629Z
-- Seed san pham E2E-L4-SP01 (du 10 san pham Active cho L4-CP-06)

      INSERT INTO Products (Id, CategoryId, Name, Sku, StandardListedPrice, Description,
                            Specifications, ImageUrl, Unit, IsDiscontinued, AverageRating, ReviewCount)
      VALUES (NEWID(), 'BC7B7B78-9319-4574-8F99-01A6CBFB7D5E', N'E2E-L4 San pham kiem thu 1', N'E2E-L4-SP01',
              50000, N'San pham do bo E2E L4 tao ra', N'', N'', N'Cái', 0, 0, 0);

-- 2026-08-21T02:23:51.779Z
-- Seed san pham E2E-L4-SP02 (du 10 san pham Active cho L4-CP-06)

      INSERT INTO Products (Id, CategoryId, Name, Sku, StandardListedPrice, Description,
                            Specifications, ImageUrl, Unit, IsDiscontinued, AverageRating, ReviewCount)
      VALUES (NEWID(), 'BC7B7B78-9319-4574-8F99-01A6CBFB7D5E', N'E2E-L4 San pham kiem thu 2', N'E2E-L4-SP02',
              50000, N'San pham do bo E2E L4 tao ra', N'', N'', N'Cái', 0, 0, 0);

-- 2026-08-21T02:23:51.924Z
-- Seed san pham E2E-L4-SP03 (du 10 san pham Active cho L4-CP-06)

      INSERT INTO Products (Id, CategoryId, Name, Sku, StandardListedPrice, Description,
                            Specifications, ImageUrl, Unit, IsDiscontinued, AverageRating, ReviewCount)
      VALUES (NEWID(), 'BC7B7B78-9319-4574-8F99-01A6CBFB7D5E', N'E2E-L4 San pham kiem thu 3', N'E2E-L4-SP03',
              50000, N'San pham do bo E2E L4 tao ra', N'', N'', N'Cái', 0, 0, 0);

-- 2026-08-21T02:23:52.215Z
-- Seed nha cung cap SUP-01 cho L4-UJ-09

    INSERT INTO Suppliers (Id, Name, Code, ContactPerson, Phone, Email, Address, TaxCode, IsActive, CreatedAt)
    VALUES (NEWID(), N'E2E-L4 Nha cung cap kiem thu', N'SUP-01', N'Nguoi lien he',
            '0900000001', N'sup01@viettien.test', N'E2E-L4 Dia chi NCC',
            N'0100000000', 1, SYSUTCDATETIME());

-- 2026-08-21T02:25:22.527Z
-- L4-SM-02 thu hoi refresh token cua customer.test
UPDATE Users SET RefreshToken = NULL, RefreshTokenExpiryTime = NULL
       WHERE Id = '77777777-7777-7777-7777-777777777777';

-- 2026-08-21T02:25:37.960Z
-- L4-SM-05 day snapshot gio lui 24:00:01

      UPDATE c SET c.UpdatedAt = DATEADD(SECOND, -1, DATEADD(HOUR, -24, SYSUTCDATETIME()))
      FROM Carts c
      JOIN CustomerProfiles p ON p.Id = c.CustomerProfileId
      WHERE p.UserId = '77777777-7777-7777-7777-777777777777';

-- 2026-08-21T02:25:54.423Z
-- L4-AM-07 dua don VT20260821022554215 ve trang thai da giao de mo luong doi/tra
UPDATE Orders SET DeliveryStatus = 3, DeliveredAt = SYSUTCDATETIME(),
                             OrderStatus = 5
           WHERE Id = '429b3d85-ce39-4e11-a8f1-67ddcb7eb8f1';

-- 2026-08-21T02:27:43.091Z
-- Seed L4-PM-07: chuyen don 928F03AA-DC5B-4A47-92D5-500D76C26579 sang ho so khach khac de thu IDOR
UPDATE Orders SET CustomerProfileId = '152F41C7-1BF0-4DED-9D41-E3B6482D7939' WHERE Id = '928F03AA-DC5B-4A47-92D5-500D76C26579';

-- 2026-08-21T02:27:49.224Z
-- L4-AM-07 dua don VT20260821022749941 ve trang thai da giao de mo luong doi/tra
UPDATE Orders SET DeliveryStatus = 3, DeliveredAt = SYSUTCDATETIME(),
                             OrderStatus = 5
           WHERE Id = 'b1143324-87c0-4cea-a51e-ca9c1c26f43b';

-- 2026-08-21T02:29:24.355Z
-- L4-SM-02 thu hoi refresh token cua customer.test
UPDATE Users SET RefreshToken = NULL, RefreshTokenExpiryTime = NULL
       WHERE Id = '77777777-7777-7777-7777-777777777777';

-- 2026-08-21T02:29:41.354Z
-- L4-SM-05 day snapshot gio lui 24:00:01

      UPDATE c SET c.UpdatedAt = DATEADD(SECOND, -1, DATEADD(HOUR, -24, SYSUTCDATETIME()))
      FROM Carts c
      JOIN CustomerProfiles p ON p.Id = c.CustomerProfileId
      WHERE p.UserId = '77777777-7777-7777-7777-777777777777';

-- 2026-08-21T02:30:00.664Z
-- L4-AM-07 dua don VT20260821023000536 ve trang thai da giao de mo luong doi/tra
UPDATE Orders SET DeliveryStatus = 3, DeliveredAt = SYSUTCDATETIME(),
                             OrderStatus = 5
           WHERE Id = 'bbc4e7b0-c938-4b6c-ad8e-dd76e85ff9fd';
