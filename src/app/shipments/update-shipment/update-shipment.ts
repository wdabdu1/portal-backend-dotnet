import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { LookupEntity, SettingsLookupService } from '../../settings/settings-lookup.service';
import { ShipmentDetail, UpdateShipmentService } from './update-shipment.service';

@Component({
  selector: 'app-update-shipment',
  imports: [CommonModule, FormsModule],
  templateUrl: './update-shipment.html'
})
export class UpdateShipment implements OnInit {
  private cdr = inject(ChangeDetectorRef);
  private route = inject(ActivatedRoute);

  shipmentId!: number;
  detail: ShipmentDetail | null = null;
  loading = true;
  error = '';

  forwarders: LookupEntity[] = [];
  currencies: LookupEntity[] = [];

  forwarderForm = { forwarderId: null as number | null, actualShippingCost: null as number | null, currencyId: null as number | null, amountSaved: null as number | null, marineInsurance: false };
  forwarderSaving = false;
  forwarderSaved = false;

  acdForm = { processDate: '', costUsd: null as number | null, costSettledDate: '', refNumber: '' };
  acdSaving = false;
  acdSaved = false;

  constructor(private lookups: SettingsLookupService, private service: UpdateShipmentService) {}

  ngOnInit(): void {
    this.shipmentId = Number(this.route.snapshot.paramMap.get('id'));

    this.lookups.getAll<LookupEntity>('forwarders').subscribe({ next: (r) => { this.forwarders = r; this.cdr.markForCheck(); } });
    this.lookups.getAll<LookupEntity>('currencies').subscribe({ next: (r) => { this.currencies = r; this.cdr.markForCheck(); } });

    this.service.getDetail(this.shipmentId).subscribe({
      next: (detail) => {
        this.detail = detail;
        if (detail.forwarder) {
          this.forwarderForm = {
            forwarderId: detail.forwarder.forwarderId,
            actualShippingCost: detail.forwarder.actualShippingCost,
            currencyId: detail.forwarder.currencyId,
            amountSaved: detail.forwarder.amountSaved,
            marineInsurance: detail.forwarder.marineInsurance
          };
        }
        if (detail.acd) {
          this.acdForm = {
            processDate: detail.acd.processDate ?? '',
            costUsd: detail.acd.costUsd,
            costSettledDate: detail.acd.costSettledDate ?? '',
            refNumber: detail.acd.refNumber ?? ''
          };
        }
        this.loading = false;
        this.cdr.markForCheck();
      },
      error: () => {
        this.error = 'Could not load shipment.';
        this.loading = false;
        this.cdr.markForCheck();
      }
    });
  }

  saveForwarder(): void {
    this.forwarderSaving = true;
    this.forwarderSaved = false;
    this.service.saveForwarder(this.shipmentId, this.forwarderForm).subscribe({
      next: () => {
        this.forwarderSaving = false;
        this.forwarderSaved = true;
        this.cdr.markForCheck();
      },
      error: () => {
        this.forwarderSaving = false;
        this.error = 'Could not save Forwarder section.';
        this.cdr.markForCheck();
      }
    });
  }

  saveAcd(): void {
    this.acdSaving = true;
    this.acdSaved = false;
    this.service.saveAcd(this.shipmentId, {
      processDate: this.acdForm.processDate || null,
      costUsd: this.acdForm.costUsd,
      costSettledDate: this.acdForm.costSettledDate || null,
      refNumber: this.acdForm.refNumber || null
    }).subscribe({
      next: () => {
        this.acdSaving = false;
        this.acdSaved = true;
        this.cdr.markForCheck();
      },
      error: () => {
        this.acdSaving = false;
        this.error = 'Could not save ACD section.';
        this.cdr.markForCheck();
      }
    });
  }
}
