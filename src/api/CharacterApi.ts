﻿import * as axios from "axios";

export class PsCharacter {
	public id: string = "";
	public name: string = "";
	public worldID: number = 0;

	public outfitID: string | null = null;
	public outfitTag: string | null = null;
	public outfitName: string | null = null;

	public factionID: number = 0;
	public battleRank: number = 0;
	public prestige: boolean = false;
}

export class CharacterApi {
	private static _instance: CharacterApi = new CharacterApi();
	public static get(): CharacterApi { return this._instance; }

	private static _parse(elem: any): PsCharacter {
		return {
			...elem
		}
	}

	public static async getByID(charID: string): Promise<PsCharacter | null> {
        const response: axios.AxiosResponse<any> = await axios.default.get(`/api/character/${charID}`);

		if (response.status != 200) {
			return null;
		}

		const c: PsCharacter = CharacterApi._parse(response.data);
		return c;
	}

	public static async getByName(name: string): Promise<PsCharacter[]> {
		const response: axios.AxiosResponse<any> = await axios.default.get(`/api/characters/name/${name}`);

		if (response.status != 200) {
			return [];
		}

		if (Array.isArray(response.data) == false) {
			throw `Data from endpoint was not an array as expected`;
		}

		return response.data.map((iter: any) => CharacterApi._parse(iter));
	}

}