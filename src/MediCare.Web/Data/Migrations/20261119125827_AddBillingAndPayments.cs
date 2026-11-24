using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCare.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingAndPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "billing_invoice_numbers");

            migrationBuilder.CreateSequence(
                name: "billing_receipt_numbers");

            migrationBuilder.CreateTable(
                name: "billing_invoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('billing_invoice_numbers')"),
                    Token = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VoidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VoidReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_invoices", x => x.Id);
                    table.CheckConstraint("CK_invoice_money", "\"Total\" BETWEEN 0 AND 999999999.99");
                    table.CheckConstraint("CK_invoice_state", "(\"State\"='Draft' AND \"IssuedAtUtc\" IS NULL AND \"VoidedAtUtc\" IS NULL AND \"VoidReason\" IS NULL) OR (\"State\"='Issued' AND \"IssuedAtUtc\" IS NOT NULL AND \"VoidedAtUtc\" IS NULL AND \"VoidReason\" IS NULL) OR (\"State\"='Voided' AND \"IssuedAtUtc\" IS NOT NULL AND \"VoidedAtUtc\" IS NOT NULL AND length(btrim(\"VoidReason\"))>0)");
                    table.CheckConstraint("CK_invoice_token", "\"Token\"<>'00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_billing_invoices_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_billing_invoices_patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "billing_services",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Category = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_services", x => x.Id);
                    table.CheckConstraint("CK_billing_service_code", "length(btrim(\"Code\"))>0 AND \"Code\"=upper(btrim(\"Code\"))");
                });

            migrationBuilder.CreateTable(
                name: "billing_payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('billing_receipt_numbers')"),
                    Token = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    Method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BankReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ReversesId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_payments", x => x.Id);
                    table.CheckConstraint("CK_payment_method", "(\"ReversesId\" IS NOT NULL AND \"Method\"='Reversal' AND \"BankReference\" IS NULL AND length(btrim(\"Reason\"))>0) OR (\"ReversesId\" IS NULL AND \"Reason\" IS NULL AND ((\"Method\"='Cash' AND \"BankReference\" IS NULL) OR (\"Method\"='BankTransfer' AND length(btrim(\"BankReference\"))>0)))");
                    table.CheckConstraint("CK_payment_money", "\"Amount\">0 AND \"Amount\"<=999999999.99");
                    table.CheckConstraint("CK_payment_token", "\"Token\"<>'00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_billing_payments_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_billing_payments_billing_invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "billing_invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_billing_payments_billing_payments_ReversesId",
                        column: x => x.ReversesId,
                        principalTable: "billing_payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "billing_prices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    EffectiveAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_prices", x => x.Id);
                    table.CheckConstraint("CK_billing_price_money", "\"UnitPrice\" BETWEEN 0 AND 999999999.99");
                    table.CheckConstraint("CK_billing_price_time", "isfinite(\"EffectiveAtUtc\") AND isfinite(\"CreatedAtUtc\")");
                    table.ForeignKey(
                        name: "FK_billing_prices_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_billing_prices_billing_services_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "billing_services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "patient_charges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Token = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    PriceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    LabRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    PharmacyDispenseId = table.Column<Guid>(type: "uuid", nullable: true),
                    AdmissionId = table.Column<Guid>(type: "uuid", nullable: true),
                    OutpatientVisitId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActorUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_patient_charges", x => x.Id);
                    table.CheckConstraint("CK_charge_money", "\"Quantity\">0 AND \"Quantity\"<=999999 AND \"UnitPrice\" BETWEEN 0 AND 999999999.99 AND \"Total\"=round(\"Quantity\"*\"UnitPrice\",2) AND \"Total\"<=999999999.99");
                    table.CheckConstraint("CK_charge_source", "num_nonnulls(\"AppointmentId\",\"LabRequestId\",\"PharmacyDispenseId\",\"AdmissionId\",\"OutpatientVisitId\")<=1");
                    table.CheckConstraint("CK_charge_token", "\"Token\"<>'00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_patient_charges_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_patient_charges_admissions_AdmissionId",
                        column: x => x.AdmissionId,
                        principalTable: "admissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_patient_charges_appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_patient_charges_billing_prices_PriceId",
                        column: x => x.PriceId,
                        principalTable: "billing_prices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_patient_charges_lab_requests_LabRequestId",
                        column: x => x.LabRequestId,
                        principalTable: "lab_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_patient_charges_outpatient_visits_OutpatientVisitId",
                        column: x => x.OutpatientVisitId,
                        principalTable: "outpatient_visits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_patient_charges_patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_patient_charges_pharmacy_dispenses_PharmacyDispenseId",
                        column: x => x.PharmacyDispenseId,
                        principalTable: "pharmacy_dispenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "billing_invoice_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChargeId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_invoice_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_billing_invoice_lines_billing_invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "billing_invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_billing_invoice_lines_patient_charges_ChargeId",
                        column: x => x.ChargeId,
                        principalTable: "patient_charges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_billing_invoice_lines_ChargeId",
                table: "billing_invoice_lines",
                column: "ChargeId");

            migrationBuilder.CreateIndex(
                name: "IX_billing_invoice_lines_InvoiceId_ChargeId",
                table: "billing_invoice_lines",
                columns: new[] { "InvoiceId", "ChargeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_billing_invoices_ActorUserId",
                table: "billing_invoices",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_billing_invoices_Number",
                table: "billing_invoices",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_billing_invoices_PatientId",
                table: "billing_invoices",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_billing_invoices_State_CreatedAtUtc",
                table: "billing_invoices",
                columns: new[] { "State", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_billing_invoices_Token",
                table: "billing_invoices",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_billing_payments_ActorUserId",
                table: "billing_payments",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_billing_payments_BankReference",
                table: "billing_payments",
                column: "BankReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_billing_payments_InvoiceId",
                table: "billing_payments",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_billing_payments_Number",
                table: "billing_payments",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_billing_payments_RecordedAtUtc",
                table: "billing_payments",
                column: "RecordedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_billing_payments_ReversesId",
                table: "billing_payments",
                column: "ReversesId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_billing_payments_Token",
                table: "billing_payments",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_billing_prices_ActorUserId",
                table: "billing_prices",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_billing_prices_ServiceId_EffectiveAtUtc",
                table: "billing_prices",
                columns: new[] { "ServiceId", "EffectiveAtUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_billing_services_Category_IsActive",
                table: "billing_services",
                columns: new[] { "Category", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_billing_services_Code",
                table: "billing_services",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_patient_charges_ActorUserId",
                table: "patient_charges",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_patient_charges_AdmissionId",
                table: "patient_charges",
                column: "AdmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_patient_charges_AppointmentId",
                table: "patient_charges",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_patient_charges_LabRequestId",
                table: "patient_charges",
                column: "LabRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_patient_charges_OutpatientVisitId",
                table: "patient_charges",
                column: "OutpatientVisitId");

            migrationBuilder.CreateIndex(
                name: "IX_patient_charges_PatientId_CreatedAtUtc",
                table: "patient_charges",
                columns: new[] { "PatientId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_patient_charges_PharmacyDispenseId",
                table: "patient_charges",
                column: "PharmacyDispenseId");

            migrationBuilder.CreateIndex(
                name: "IX_patient_charges_PriceId",
                table: "patient_charges",
                column: "PriceId");

            migrationBuilder.CreateIndex(
                name: "IX_patient_charges_Token",
                table: "patient_charges",
                column: "Token",
                unique: true);
            migrationBuilder.Sql(BillingDatabaseSql.Guards);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Financial history cannot be removed by automatic migration rollback.");
        }
    }
}
