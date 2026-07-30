import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { API_URL } from '../../api-config';

export interface ShipmentForwarder {
  forwarderId: number | null;
  actualShippingCost: number | null;
  currencyId: number | null;
  actualShippingCostUsd: number | null;
  amountSaved: number | null;
  marineInsurance: boolean;
}

export interface ShipmentAcd {
  processDate: string | null;
  costUsd: number | null;
  costSettledDate: string | null;
  refNumber: string | null;
}

export interface ShipmentDetail {
  id: number;
  blAwbNo: string;
  poNumber: string;
  status: string;
  forwarder: ShipmentForwarder | null;
  acd: ShipmentAcd | null;
}

@Injectable({ providedIn: 'root' })
export class UpdateShipmentService {
  constructor(private http: HttpClient) {}

  getDetail(shipmentId: number) {
    return this.http.get<ShipmentDetail>(`${API_URL}/shipments/${shipmentId}/detail`);
  }

  saveForwarder(shipmentId: number, req: Partial<ShipmentForwarder>) {
    return this.http.put(`${API_URL}/shipments/${shipmentId}/forwarder`, req);
  }

  saveAcd(shipmentId: number, req: Partial<ShipmentAcd>) {
    return this.http.put(`${API_URL}/shipments/${shipmentId}/acd`, req);
  }
}
