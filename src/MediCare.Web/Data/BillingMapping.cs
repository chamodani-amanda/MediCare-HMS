using MediCare.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Web.Data;

internal static class BillingMapping
{
    internal static void ConfigureBilling(this ModelBuilder b)
    {
        b.HasSequence<long>("billing_invoice_numbers");b.HasSequence<long>("billing_receipt_numbers");
        b.Entity<BillingService>(e=>{
            e.ToTable("billing_services",t=>t.HasCheckConstraint("CK_billing_service_code","length(btrim(\"Code\"))>0 AND \"Code\"=upper(btrim(\"Code\"))"));
            e.HasIndex(x=>x.Code).IsUnique();e.Property(x=>x.Version).IsConcurrencyToken();e.HasIndex(x=>new{x.Category,x.IsActive});
        });
        b.Entity<BillingPrice>(e=>{
            e.ToTable("billing_prices",t=>{t.HasCheckConstraint("CK_billing_price_money","\"UnitPrice\" BETWEEN 0 AND 999999999.99");t.HasCheckConstraint("CK_billing_price_time","isfinite(\"EffectiveAtUtc\") AND isfinite(\"CreatedAtUtc\")");});
            e.Property(x=>x.UnitPrice).HasPrecision(14,2);e.HasIndex(x=>new{x.ServiceId,x.EffectiveAtUtc}).IsUnique();
            e.HasOne(x=>x.Service).WithMany().HasForeignKey(x=>x.ServiceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<IdentityUser>().WithMany().HasForeignKey(x=>x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<PatientCharge>(e=>{
            e.ToTable("patient_charges",t=>{
                t.HasCheckConstraint("CK_charge_money","\"Quantity\">0 AND \"Quantity\"<=999999 AND \"UnitPrice\" BETWEEN 0 AND 999999999.99 AND \"Total\"=round(\"Quantity\"*\"UnitPrice\",2) AND \"Total\"<=999999999.99");
                t.HasCheckConstraint("CK_charge_source","num_nonnulls(\"AppointmentId\",\"LabRequestId\",\"PharmacyDispenseId\",\"AdmissionId\",\"OutpatientVisitId\")<=1");
                t.HasCheckConstraint("CK_charge_token","\"Token\"<>'00000000-0000-0000-0000-000000000000'::uuid");
            });
            e.Property(x=>x.Quantity).HasPrecision(12,3);e.Property(x=>x.UnitPrice).HasPrecision(14,2);e.Property(x=>x.Total).HasPrecision(14,2);e.HasIndex(x=>x.Token).IsUnique();e.HasIndex(x=>new{x.PatientId,x.CreatedAtUtc});
            e.HasOne(x=>x.Price).WithMany().HasForeignKey(x=>x.PriceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Patient>().WithMany().HasForeignKey(x=>x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Appointment>().WithMany().HasForeignKey(x=>x.AppointmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<LabRequest>().WithMany().HasForeignKey(x=>x.LabRequestId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PharmacyDispense>().WithMany().HasForeignKey(x=>x.PharmacyDispenseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Admission>().WithMany().HasForeignKey(x=>x.AdmissionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<OutpatientVisit>().WithMany().HasForeignKey(x=>x.OutpatientVisitId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<IdentityUser>().WithMany().HasForeignKey(x=>x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<BillingInvoice>(e=>{
            e.ToTable("billing_invoices",t=>{
                t.HasCheckConstraint("CK_invoice_state","(\"State\"='Draft' AND \"IssuedAtUtc\" IS NULL AND \"VoidedAtUtc\" IS NULL AND \"VoidReason\" IS NULL) OR (\"State\"='Issued' AND \"IssuedAtUtc\" IS NOT NULL AND \"VoidedAtUtc\" IS NULL AND \"VoidReason\" IS NULL) OR (\"State\"='Voided' AND \"IssuedAtUtc\" IS NOT NULL AND \"VoidedAtUtc\" IS NOT NULL AND length(btrim(\"VoidReason\"))>0)");
                t.HasCheckConstraint("CK_invoice_money","\"Total\" BETWEEN 0 AND 999999999.99");t.HasCheckConstraint("CK_invoice_token","\"Token\"<>'00000000-0000-0000-0000-000000000000'::uuid");
            });
            e.Property(x=>x.State).HasConversion<string>().HasMaxLength(20);e.Property(x=>x.Total).HasPrecision(14,2);e.Property(x=>x.Version).IsConcurrencyToken();e.Property(x=>x.Number).HasDefaultValueSql("nextval('billing_invoice_numbers')");e.HasIndex(x=>x.Number).IsUnique();e.HasIndex(x=>x.Token).IsUnique();e.HasIndex(x=>new{x.State,x.CreatedAtUtc});e.HasIndex(x=>x.PatientId);
            e.HasOne<Patient>().WithMany().HasForeignKey(x=>x.PatientId).OnDelete(DeleteBehavior.Restrict);e.HasOne<IdentityUser>().WithMany().HasForeignKey(x=>x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<BillingInvoiceLine>(e=>{
            e.ToTable("billing_invoice_lines");e.HasIndex(x=>new{x.InvoiceId,x.ChargeId}).IsUnique();e.HasIndex(x=>x.ChargeId);
            e.HasOne(x=>x.Invoice).WithMany(x=>x.Lines).HasForeignKey(x=>x.InvoiceId).OnDelete(DeleteBehavior.Restrict);e.HasOne(x=>x.Charge).WithMany().HasForeignKey(x=>x.ChargeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<BillingPayment>(e=>{
            e.ToTable("billing_payments",t=>{
                t.HasCheckConstraint("CK_payment_money","\"Amount\">0 AND \"Amount\"<=999999999.99");
                t.HasCheckConstraint("CK_payment_method","(\"ReversesId\" IS NOT NULL AND \"Method\"='Reversal' AND \"BankReference\" IS NULL AND length(btrim(\"Reason\"))>0) OR (\"ReversesId\" IS NULL AND \"Reason\" IS NULL AND ((\"Method\"='Cash' AND \"BankReference\" IS NULL) OR (\"Method\"='BankTransfer' AND length(btrim(\"BankReference\"))>0)))");
                t.HasCheckConstraint("CK_payment_token","\"Token\"<>'00000000-0000-0000-0000-000000000000'::uuid");
            });
            e.Property(x=>x.Amount).HasPrecision(14,2);e.Property(x=>x.Number).HasDefaultValueSql("nextval('billing_receipt_numbers')");e.HasIndex(x=>x.Number).IsUnique();e.HasIndex(x=>x.Token).IsUnique();e.HasIndex(x=>x.ReversesId).IsUnique();e.HasIndex(x=>x.BankReference).IsUnique();e.HasIndex(x=>x.RecordedAtUtc);
            e.HasOne(x=>x.Invoice).WithMany().HasForeignKey(x=>x.InvoiceId).OnDelete(DeleteBehavior.Restrict);e.HasOne<BillingPayment>().WithMany().HasForeignKey(x=>x.ReversesId).OnDelete(DeleteBehavior.Restrict);e.HasOne<IdentityUser>().WithMany().HasForeignKey(x=>x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
