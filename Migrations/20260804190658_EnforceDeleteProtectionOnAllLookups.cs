using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace portal_backend_dotnet.Migrations
{
    /// <inheritdoc />
    public partial class EnforceDeleteProtectionOnAllLookups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrderLineItems_Currencies_CurrencyId",
                table: "PurchaseOrderLineItems");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrderLineItems_ModelProducts_ModelProductId",
                table: "PurchaseOrderLineItems");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrderLineItems_ProductCategories_ProductCategoryId",
                table: "PurchaseOrderLineItems");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrderLineItems_ProductTypes_ProductTypeId",
                table: "PurchaseOrderLineItems");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrderLineItems_UnitsOfMeasure_UnitOfMeasureId",
                table: "PurchaseOrderLineItems");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrders_ApprovalTypes_ApprovalTypeId",
                table: "PurchaseOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrders_BusinessUnits_BusinessUnitId",
                table: "PurchaseOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrders_Divisions_DivisionId",
                table: "PurchaseOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrders_Incoterms_IncotermId",
                table: "PurchaseOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrders_OriginCountries_OriginCountryId",
                table: "PurchaseOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrders_PaymentTerms_SupplierPaymentTermId",
                table: "PurchaseOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrders_ShipmentModes_ShipmentModeId",
                table: "PurchaseOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_ShipmentBankings_Couriers_OsDocDispatchedViaId",
                table: "ShipmentBankings");

            migrationBuilder.DropForeignKey(
                name: "FK_ShipmentBankings_Currencies_CollectionCurrencyId",
                table: "ShipmentBankings");

            migrationBuilder.DropForeignKey(
                name: "FK_ShipmentBankings_ReceiverBanks_ReceivingBankId",
                table: "ShipmentBankings");

            migrationBuilder.DropForeignKey(
                name: "FK_ShipmentBankings_SenderBanks_SenderBankId",
                table: "ShipmentBankings");

            migrationBuilder.DropForeignKey(
                name: "FK_ShipmentBankings_Tenors_TenorId",
                table: "ShipmentBankings");

            migrationBuilder.DropForeignKey(
                name: "FK_ShipmentForwarders_Currencies_CurrencyId",
                table: "ShipmentForwarders");

            migrationBuilder.DropForeignKey(
                name: "FK_ShipmentForwarders_Forwarders_ForwarderId",
                table: "ShipmentForwarders");

            migrationBuilder.DropForeignKey(
                name: "FK_Shipments_ShippingLines_ShippingLineId",
                table: "Shipments");

            migrationBuilder.DropForeignKey(
                name: "FK_ShipmentSupplierFullSets_Couriers_FsDispatchedViaId",
                table: "ShipmentSupplierFullSets");

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrderLineItems_Currencies_CurrencyId",
                table: "PurchaseOrderLineItems",
                column: "CurrencyId",
                principalTable: "Currencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrderLineItems_ModelProducts_ModelProductId",
                table: "PurchaseOrderLineItems",
                column: "ModelProductId",
                principalTable: "ModelProducts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrderLineItems_ProductCategories_ProductCategoryId",
                table: "PurchaseOrderLineItems",
                column: "ProductCategoryId",
                principalTable: "ProductCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrderLineItems_ProductTypes_ProductTypeId",
                table: "PurchaseOrderLineItems",
                column: "ProductTypeId",
                principalTable: "ProductTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrderLineItems_UnitsOfMeasure_UnitOfMeasureId",
                table: "PurchaseOrderLineItems",
                column: "UnitOfMeasureId",
                principalTable: "UnitsOfMeasure",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrders_ApprovalTypes_ApprovalTypeId",
                table: "PurchaseOrders",
                column: "ApprovalTypeId",
                principalTable: "ApprovalTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrders_BusinessUnits_BusinessUnitId",
                table: "PurchaseOrders",
                column: "BusinessUnitId",
                principalTable: "BusinessUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrders_Divisions_DivisionId",
                table: "PurchaseOrders",
                column: "DivisionId",
                principalTable: "Divisions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrders_Incoterms_IncotermId",
                table: "PurchaseOrders",
                column: "IncotermId",
                principalTable: "Incoterms",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrders_OriginCountries_OriginCountryId",
                table: "PurchaseOrders",
                column: "OriginCountryId",
                principalTable: "OriginCountries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrders_PaymentTerms_SupplierPaymentTermId",
                table: "PurchaseOrders",
                column: "SupplierPaymentTermId",
                principalTable: "PaymentTerms",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrders_ShipmentModes_ShipmentModeId",
                table: "PurchaseOrders",
                column: "ShipmentModeId",
                principalTable: "ShipmentModes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ShipmentBankings_Couriers_OsDocDispatchedViaId",
                table: "ShipmentBankings",
                column: "OsDocDispatchedViaId",
                principalTable: "Couriers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ShipmentBankings_Currencies_CollectionCurrencyId",
                table: "ShipmentBankings",
                column: "CollectionCurrencyId",
                principalTable: "Currencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ShipmentBankings_ReceiverBanks_ReceivingBankId",
                table: "ShipmentBankings",
                column: "ReceivingBankId",
                principalTable: "ReceiverBanks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ShipmentBankings_SenderBanks_SenderBankId",
                table: "ShipmentBankings",
                column: "SenderBankId",
                principalTable: "SenderBanks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ShipmentBankings_Tenors_TenorId",
                table: "ShipmentBankings",
                column: "TenorId",
                principalTable: "Tenors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ShipmentForwarders_Currencies_CurrencyId",
                table: "ShipmentForwarders",
                column: "CurrencyId",
                principalTable: "Currencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ShipmentForwarders_Forwarders_ForwarderId",
                table: "ShipmentForwarders",
                column: "ForwarderId",
                principalTable: "Forwarders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Shipments_ShippingLines_ShippingLineId",
                table: "Shipments",
                column: "ShippingLineId",
                principalTable: "ShippingLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ShipmentSupplierFullSets_Couriers_FsDispatchedViaId",
                table: "ShipmentSupplierFullSets",
                column: "FsDispatchedViaId",
                principalTable: "Couriers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrderLineItems_Currencies_CurrencyId",
                table: "PurchaseOrderLineItems");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrderLineItems_ModelProducts_ModelProductId",
                table: "PurchaseOrderLineItems");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrderLineItems_ProductCategories_ProductCategoryId",
                table: "PurchaseOrderLineItems");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrderLineItems_ProductTypes_ProductTypeId",
                table: "PurchaseOrderLineItems");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrderLineItems_UnitsOfMeasure_UnitOfMeasureId",
                table: "PurchaseOrderLineItems");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrders_ApprovalTypes_ApprovalTypeId",
                table: "PurchaseOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrders_BusinessUnits_BusinessUnitId",
                table: "PurchaseOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrders_Divisions_DivisionId",
                table: "PurchaseOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrders_Incoterms_IncotermId",
                table: "PurchaseOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrders_OriginCountries_OriginCountryId",
                table: "PurchaseOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrders_PaymentTerms_SupplierPaymentTermId",
                table: "PurchaseOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrders_ShipmentModes_ShipmentModeId",
                table: "PurchaseOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_ShipmentBankings_Couriers_OsDocDispatchedViaId",
                table: "ShipmentBankings");

            migrationBuilder.DropForeignKey(
                name: "FK_ShipmentBankings_Currencies_CollectionCurrencyId",
                table: "ShipmentBankings");

            migrationBuilder.DropForeignKey(
                name: "FK_ShipmentBankings_ReceiverBanks_ReceivingBankId",
                table: "ShipmentBankings");

            migrationBuilder.DropForeignKey(
                name: "FK_ShipmentBankings_SenderBanks_SenderBankId",
                table: "ShipmentBankings");

            migrationBuilder.DropForeignKey(
                name: "FK_ShipmentBankings_Tenors_TenorId",
                table: "ShipmentBankings");

            migrationBuilder.DropForeignKey(
                name: "FK_ShipmentForwarders_Currencies_CurrencyId",
                table: "ShipmentForwarders");

            migrationBuilder.DropForeignKey(
                name: "FK_ShipmentForwarders_Forwarders_ForwarderId",
                table: "ShipmentForwarders");

            migrationBuilder.DropForeignKey(
                name: "FK_Shipments_ShippingLines_ShippingLineId",
                table: "Shipments");

            migrationBuilder.DropForeignKey(
                name: "FK_ShipmentSupplierFullSets_Couriers_FsDispatchedViaId",
                table: "ShipmentSupplierFullSets");

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrderLineItems_Currencies_CurrencyId",
                table: "PurchaseOrderLineItems",
                column: "CurrencyId",
                principalTable: "Currencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrderLineItems_ModelProducts_ModelProductId",
                table: "PurchaseOrderLineItems",
                column: "ModelProductId",
                principalTable: "ModelProducts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrderLineItems_ProductCategories_ProductCategoryId",
                table: "PurchaseOrderLineItems",
                column: "ProductCategoryId",
                principalTable: "ProductCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrderLineItems_ProductTypes_ProductTypeId",
                table: "PurchaseOrderLineItems",
                column: "ProductTypeId",
                principalTable: "ProductTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrderLineItems_UnitsOfMeasure_UnitOfMeasureId",
                table: "PurchaseOrderLineItems",
                column: "UnitOfMeasureId",
                principalTable: "UnitsOfMeasure",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrders_ApprovalTypes_ApprovalTypeId",
                table: "PurchaseOrders",
                column: "ApprovalTypeId",
                principalTable: "ApprovalTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrders_BusinessUnits_BusinessUnitId",
                table: "PurchaseOrders",
                column: "BusinessUnitId",
                principalTable: "BusinessUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrders_Divisions_DivisionId",
                table: "PurchaseOrders",
                column: "DivisionId",
                principalTable: "Divisions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrders_Incoterms_IncotermId",
                table: "PurchaseOrders",
                column: "IncotermId",
                principalTable: "Incoterms",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrders_OriginCountries_OriginCountryId",
                table: "PurchaseOrders",
                column: "OriginCountryId",
                principalTable: "OriginCountries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrders_PaymentTerms_SupplierPaymentTermId",
                table: "PurchaseOrders",
                column: "SupplierPaymentTermId",
                principalTable: "PaymentTerms",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrders_ShipmentModes_ShipmentModeId",
                table: "PurchaseOrders",
                column: "ShipmentModeId",
                principalTable: "ShipmentModes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ShipmentBankings_Couriers_OsDocDispatchedViaId",
                table: "ShipmentBankings",
                column: "OsDocDispatchedViaId",
                principalTable: "Couriers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ShipmentBankings_Currencies_CollectionCurrencyId",
                table: "ShipmentBankings",
                column: "CollectionCurrencyId",
                principalTable: "Currencies",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ShipmentBankings_ReceiverBanks_ReceivingBankId",
                table: "ShipmentBankings",
                column: "ReceivingBankId",
                principalTable: "ReceiverBanks",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ShipmentBankings_SenderBanks_SenderBankId",
                table: "ShipmentBankings",
                column: "SenderBankId",
                principalTable: "SenderBanks",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ShipmentBankings_Tenors_TenorId",
                table: "ShipmentBankings",
                column: "TenorId",
                principalTable: "Tenors",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ShipmentForwarders_Currencies_CurrencyId",
                table: "ShipmentForwarders",
                column: "CurrencyId",
                principalTable: "Currencies",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ShipmentForwarders_Forwarders_ForwarderId",
                table: "ShipmentForwarders",
                column: "ForwarderId",
                principalTable: "Forwarders",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Shipments_ShippingLines_ShippingLineId",
                table: "Shipments",
                column: "ShippingLineId",
                principalTable: "ShippingLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ShipmentSupplierFullSets_Couriers_FsDispatchedViaId",
                table: "ShipmentSupplierFullSets",
                column: "FsDispatchedViaId",
                principalTable: "Couriers",
                principalColumn: "Id");
        }
    }
}
