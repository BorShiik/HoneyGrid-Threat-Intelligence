/** A geolocated attack origin rendered as a node + arc on the globe. */
export interface ThreatSource {
  country: string;
  countryName: string;
  lat: number;
  lng: number;
  count: number;
}
