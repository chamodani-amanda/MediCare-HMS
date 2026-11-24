using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCare.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPharmacyManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AuthorizedQuantity",
                table: "medical_prescriptions",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DispensingUnit",
                table: "medical_prescriptions",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LineageId",
                table: "medical_prescriptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MedicineId",
                table: "medical_prescriptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "medicines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    GenericName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Strength = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Formulation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DispensingUnit = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    LowStockThreshold = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medicines", x => x.Id);
                    table.CheckConstraint("CK_medicines_fields", "length(btrim(\"Code\")) > 0 AND \"Code\" = upper(btrim(\"Code\")) AND length(btrim(\"Name\")) > 0 AND length(btrim(\"GenericName\")) > 0 AND length(btrim(\"Strength\")) > 0 AND length(btrim(\"Formulation\")) > 0 AND length(btrim(\"DispensingUnit\")) > 0");
                    table.CheckConstraint("CK_medicines_threshold", "\"LowStockThreshold\" BETWEEN 0 AND 1000000000");
                });

            migrationBuilder.CreateTable(
                name: "pharmacy_dispenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Token = table.Column<Guid>(type: "uuid", nullable: false),
                    PrescriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ReversesId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pharmacy_dispenses", x => x.Id);
                    table.CheckConstraint("CK_pharmacy_dispenses_quantity", "(\"ReversesId\" IS NULL AND \"Quantity\" > 0) OR (\"ReversesId\" IS NOT NULL AND \"Quantity\" < 0 AND length(btrim(\"Reason\")) > 0 AND \"Reason\" IS NOT NULL)");
                    table.CheckConstraint("CK_pharmacy_dispenses_token", "\"Token\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"LineageId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_pharmacy_dispenses_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pharmacy_dispenses_medical_prescriptions_PrescriptionId",
                        column: x => x.PrescriptionId,
                        principalTable: "medical_prescriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pharmacy_dispenses_pharmacy_dispenses_ReversesId",
                        column: x => x.ReversesId,
                        principalTable: "pharmacy_dispenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "medicine_batches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MedicineId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchNumber = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Supplier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ManufacturedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpiresOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AvailableQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medicine_batches", x => x.Id);
                    table.CheckConstraint("CK_medicine_batches_balance", "\"AvailableQuantity\" BETWEEN 0 AND 1000000000");
                    table.CheckConstraint("CK_medicine_batches_fields", "length(btrim(\"BatchNumber\")) > 0 AND \"BatchNumber\" = upper(btrim(\"BatchNumber\")) AND length(btrim(\"Supplier\")) > 0 AND (\"ManufacturedOn\" IS NULL OR \"ManufacturedOn\" <= \"ExpiresOn\")");
                    table.ForeignKey(
                        name: "FK_medicine_batches_medicines_MedicineId",
                        column: x => x.MedicineId,
                        principalTable: "medicines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_movements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpectedBatchVersion = table.Column<Guid>(type: "uuid", nullable: true),
                    Token = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    DispenseId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_movements", x => x.Id);
                    table.CheckConstraint("CK_inventory_movements_fields", "\"Token\" <> '00000000-0000-0000-0000-000000000000'::uuid AND length(btrim(\"Reason\")) > 0 AND abs(\"Quantity\") <= 1000000000");
                    table.CheckConstraint("CK_inventory_movements_kind", "(\"Kind\" = 'Receive' AND \"Quantity\" > 0 AND \"DispenseId\" IS NULL) OR (\"Kind\" = 'Adjust' AND \"Quantity\" <> 0 AND \"DispenseId\" IS NULL) OR (\"Kind\" = 'Dispense' AND \"Quantity\" < 0 AND \"DispenseId\" IS NOT NULL) OR (\"Kind\" = 'QuarantineReturn' AND \"Quantity\" > 0 AND \"DispenseId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_inventory_movements_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_movements_medicine_batches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "medicine_batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_movements_pharmacy_dispenses_DispenseId",
                        column: x => x.DispenseId,
                        principalTable: "pharmacy_dispenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_medical_prescriptions_LineageId",
                table: "medical_prescriptions",
                column: "LineageId");

            migrationBuilder.CreateIndex(
                name: "IX_medical_prescriptions_MedicalRecordRevisionId_LineageId",
                table: "medical_prescriptions",
                columns: new[] { "MedicalRecordRevisionId", "LineageId" },
                unique: true,
                filter: "\"LineageId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_medical_prescriptions_MedicineId",
                table: "medical_prescriptions",
                column: "MedicineId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_medical_prescriptions_authorization",
                table: "medical_prescriptions",
                sql: "(\"MedicineId\" IS NULL AND \"AuthorizedQuantity\" IS NULL AND \"DispensingUnit\" IS NULL) OR (\"MedicineId\" IS NOT NULL AND \"AuthorizedQuantity\" > 0 AND \"AuthorizedQuantity\" <= 1000000000 AND \"AuthorizedQuantity\" IS NOT NULL AND length(btrim(\"DispensingUnit\")) > 0 AND \"DispensingUnit\" IS NOT NULL AND \"LineageId\" IS NOT NULL AND \"LineageId\" <> '00000000-0000-0000-0000-000000000000'::uuid)");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_movements_ActorUserId",
                table: "inventory_movements",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_movements_BatchId_OccurredAtUtc",
                table: "inventory_movements",
                columns: new[] { "BatchId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_movements_DispenseId",
                table: "inventory_movements",
                column: "DispenseId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_movements_Token",
                table: "inventory_movements",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_medicine_batches_ExpiresOn",
                table: "medicine_batches",
                column: "ExpiresOn");

            migrationBuilder.CreateIndex(
                name: "IX_medicine_batches_MedicineId_BatchNumber",
                table: "medicine_batches",
                columns: new[] { "MedicineId", "BatchNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_medicines_Code",
                table: "medicines",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pharmacy_dispenses_ActorUserId",
                table: "pharmacy_dispenses",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_pharmacy_dispenses_LineageId",
                table: "pharmacy_dispenses",
                column: "LineageId");

            migrationBuilder.CreateIndex(
                name: "IX_pharmacy_dispenses_PrescriptionId",
                table: "pharmacy_dispenses",
                column: "PrescriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_pharmacy_dispenses_ReversesId",
                table: "pharmacy_dispenses",
                column: "ReversesId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pharmacy_dispenses_Token",
                table: "pharmacy_dispenses",
                column: "Token",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_medical_prescriptions_medicines_MedicineId",
                table: "medical_prescriptions",
                column: "MedicineId",
                principalTable: "medicines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql(PharmacyDatabaseSql.Guards);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Pharmacy rollback requires a separately reviewed data-preserving procedure.");
        }
    }
}