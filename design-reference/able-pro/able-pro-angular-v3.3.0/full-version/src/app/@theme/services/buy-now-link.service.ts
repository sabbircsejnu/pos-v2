import { Injectable } from '@angular/core';

@Injectable({
  providedIn: 'root'
})
export class BuyNowLinkService {
  buyNowLink: string = '';
  queryString: string = '';
  isPhoenix: boolean = false;
  getQueryParams: { [key: string]: string } = {};

  setBuyNowLink(urlValue: URLSearchParams): void {
    const ispValue = urlValue.get('isp');
    const refValue = urlValue.get('ref');
    urlValue.forEach((value, key) => {
      this.getQueryParams[key] = value;
    });

    const params = new URLSearchParams(this.getQueryParams).toString();

    if (ispValue === '1') {
      if (refValue === 'tf') {
        this.buyNowLink = 'https://1.envato.market/XYAZnb';
        this.queryString = params ? `?${params}` : '';
        this.isPhoenix = true;
      } else {
        this.buyNowLink = 'https://codedthemes.com/item/able-pro-angular-admin-template/';
        this.queryString = params ? `?${params}` : '';
        this.isPhoenix = false;
      }
    } else {
      if (refValue === 'tf') {
        this.buyNowLink = 'https://1.envato.market/zNkqj6';
        this.queryString = params ? `?${params}` : '';
        this.isPhoenix = true;
      } else {
        this.buyNowLink = 'https://codedthemes.com/item/able-pro-dashboard-templates';
        this.queryString = params ? `?${params}` : '';
        this.isPhoenix = false;
      }
    }
  }
}
