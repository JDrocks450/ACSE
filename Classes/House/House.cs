﻿using ACSE.Classes.Utilities;
using System.Reflection;

namespace ACSE
{
    public class House
    {
        public int Index;
        public int Offset;
        public HouseData Data;
        public Player Owner;

        public House(int index, int offset)
        {
            Index = index;
            Offset = offset;

            var houseSize = HouseInfo.GetHouseSize(offset, MainForm.Save_File.SaveType);
            var basement = false;
            //Console.WriteLine("House Index: " + Index);
            //Console.WriteLine("House Offset: 0x" + Offset.ToString("X"));
            //Console.WriteLine("House Size: " + HouseSize.ToString());
            if (MainForm.Save_File.SaveGeneration == SaveGeneration.N64 || MainForm.Save_File.SaveGeneration == SaveGeneration.GCN)
            {
                basement = HouseInfo.HasBasement(offset, MainForm.Save_File.SaveType);
                //Console.WriteLine("Basement: " + Basement.ToString());
            }

            // Load House Data
            var offsets = HouseInfo.GetHouseOffsets(MainForm.Save_File.SaveType);
            var saveData = MainForm.Save_File;
            var playerDataType = typeof(HouseData);
            var playerSaveInfoType = typeof(HouseOffsets);
            object boxedData = new HouseData();
            foreach (var field in playerSaveInfoType.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.GetValue(offsets) == null || field.Name.Contains("Count") ||
                    field.Name.Contains("Size")) continue;
                if (playerDataType.GetField(field.Name) == null) continue;
                if (field.FieldType != typeof(int) || (int)field.GetValue(offsets) == -1) continue;
                var currentField = playerDataType.GetField(field.Name);
                var fieldType = currentField.FieldType;
                var dataOffset = offset + (int)field.GetValue(offsets);

                if (field.Name.Equals("Room_Carpet") || field.Name.Equals("Room_Wallpaper") ||
                    field.Name.Equals("Room_Song")) continue;
                if (fieldType == typeof(byte))
                    currentField.SetValue(boxedData, saveData.ReadByte(dataOffset));
                else if (fieldType == typeof(byte[]) && playerSaveInfoType.GetField(field.Name + "Count") != null)
                    currentField.SetValue(boxedData, saveData.ReadByteArray(dataOffset,
                        (int)playerSaveInfoType.GetField(field.Name + "Count").GetValue(offsets)));
                else if (fieldType == typeof(ushort))
                    currentField.SetValue(boxedData, saveData.ReadUInt16(dataOffset, saveData.IsBigEndian));
                else if (fieldType == typeof(ushort[]))
                    currentField.SetValue(boxedData, saveData.ReadUInt16Array(dataOffset,
                        (int)playerSaveInfoType.GetField(field.Name + "Count").GetValue(offsets), saveData.IsBigEndian));
                else if (fieldType == typeof(uint))
                    currentField.SetValue(boxedData, saveData.ReadUInt32(dataOffset, saveData.IsBigEndian));
                else if (fieldType == typeof(string))
                    currentField.SetValue(boxedData, new ACString(saveData.ReadByteArray(dataOffset,
                        (int)playerSaveInfoType.GetField(field.Name + "Size").GetValue(offsets)), saveData.SaveType).Trim());
                else if (fieldType == typeof(Item))
                    if (saveData.SaveGeneration == SaveGeneration.N3DS)
                        currentField.SetValue(boxedData, new Item(saveData.ReadUInt32(dataOffset, false)));
                    else
                        currentField.SetValue(boxedData, new Item(saveData.ReadUInt16(dataOffset, saveData.IsBigEndian)));
                else if (fieldType == typeof(NL_Int32))
                {
                    var intData = saveData.ReadUInt32Array(dataOffset, 2);
                    currentField.SetValue(boxedData, new NL_Int32(intData[0], intData[1]));
                }
                else if (fieldType == typeof(ACDate) && dataOffset > 0)
                {
                    currentField.SetValue(boxedData, new ACDate(saveData.ReadByteArray(dataOffset,
                        (int)playerSaveInfoType.GetField(field.Name + "Size").GetValue(offsets))));
                }
            }
            Data = (HouseData)boxedData;

            // Load Rooms/Layers
            var itemDataSize = MainForm.Save_File.SaveGeneration == SaveGeneration.N3DS ? 4 : 2;
            const int itemsPerLayer = 256; //Offsets.Layer_Size / ItemDataSize;
            Data.Rooms = new Room[offsets.RoomCount];
            var roomNames = HouseInfo.GetRoomNames(saveData.SaveGeneration);

            for (var i = 0; i < offsets.RoomCount; i++)
            {
                var roomOffset = offset + offsets.RoomStart + i * offsets.RoomSize;
                var room = new Room
                {
                    Index = i,
                    Offset = roomOffset,
                    Name = roomNames[i],
                    Layers = new Layer[offsets.LayerCount]
                };

                if (saveData.SaveGeneration == SaveGeneration.N64 || saveData.SaveGeneration == SaveGeneration.GCN)
                {
                    room.Carpet = new Item((ushort)(0x2600 | saveData.ReadByte(roomOffset + offsets.RoomCarpet)));
                    room.Wallpaper = new Item((ushort)(0x2700 | saveData.ReadByte(roomOffset + offsets.RoomWallpaper)));
                }
                else
                {
                    room.Carpet = new Item(saveData.ReadUInt16(roomOffset + offsets.RoomCarpet, saveData.IsBigEndian));
                    room.Wallpaper = new Item(saveData.ReadUInt16(roomOffset + offsets.RoomWallpaper, saveData.IsBigEndian));
                }

                for (var x = 0; x < offsets.LayerCount; x++)
                {
                    var layerOffset = roomOffset + offsets.LayerSize * x;
                    var layer = new Layer
                    {
                        Offset = layerOffset,
                        Index = x,
                        Items = new Furniture[itemsPerLayer],
                        Parent = room
                    };

                    // Load furniture for the layer
                    for (var f = 0; f < itemsPerLayer; f++)
                    {
                        var furnitureOffset = layerOffset + f * itemDataSize;
                        if (itemDataSize == 4)
                        {
                            layer.Items[f] = new Furniture(saveData.ReadUInt32(furnitureOffset));
                        }
                        else
                        {
                            layer.Items[f] = new Furniture(saveData.ReadUInt16(furnitureOffset, saveData.IsBigEndian));
                        }
                    }

                    room.Layers[x] = layer;
                }
                Data.Rooms[i] = room;
            }
        }

        public void Write()
        {
            var saveData = MainForm.Save_File;
            var offsets = HouseInfo.GetHouseOffsets(saveData.SaveType);

            // Set House TownID & Name
            if (offsets.OwningPlayerName != -1 && Owner != null && offsets.TownId != -1)
            {
                Data.TownId = saveData.ReadUInt16(saveData.SaveDataStartOffset + MainForm.Current_Save_Info.SaveOffsets.TownId, saveData.IsBigEndian); // Might not be UInt16 in all games
            }
            if (offsets.OwningPlayerName != -1 && Owner != null && offsets.TownName != -1)
            {
                Data.TownName = saveData.ReadString(saveData.SaveDataStartOffset + MainForm.Current_Save_Info.SaveOffsets.TownName,
                    MainForm.Current_Save_Info.SaveOffsets.TownNameSize);
            }
            if (offsets.OwningPlayerName != -1 && Owner != null)
            {
                Data.OwningPlayerName = Owner.Data.Name;
            }
            if (offsets.OwningPlayerId != -1 && Owner != null)
            {
                Data.OwningPlayerId = Owner.Data.Identifier;
            }

            var houseOffsetData = typeof(HouseOffsets);
            var houseDataType = typeof(HouseData);
            foreach (var field in houseOffsetData.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.GetValue(offsets) == null || field.Name.Contains("Count") ||
                    field.Name.Contains("Size")) continue;
                if (houseDataType.GetField(field.Name) == null) continue;
                if (field.FieldType != typeof(int) || (int)field.GetValue(offsets) == -1) continue;
                var fieldType = houseDataType.GetField(field.Name).FieldType;
                var dataOffset = Offset + (int)field.GetValue(offsets);
                //MessageBox.Show("Field Name: " + Field.Name + " | Data Offset: " + DataOffset.ToString("X"));
                if (fieldType == typeof(string))
                {
                    saveData.Write(dataOffset, ACString.GetBytes((string)houseDataType.GetField(field.Name).GetValue(Data),
                        (int)houseOffsetData.GetField(field.Name + "Size").GetValue(offsets)));
                }
                else if (fieldType == typeof(byte))
                {
                    saveData.Write(dataOffset, (byte)houseDataType.GetField(field.Name).GetValue(Data));
                }
                else if (fieldType == typeof(byte[]))
                {
                    saveData.Write(dataOffset, (byte[])houseDataType.GetField(field.Name).GetValue(Data));
                }
                else if (fieldType == typeof(ushort))
                {
                    saveData.Write(dataOffset, (ushort)houseDataType.GetField(field.Name).GetValue(Data), saveData.IsBigEndian);
                }
                else if (fieldType == typeof(Item))
                {
                    if (saveData.SaveGeneration == SaveGeneration.N3DS)
                    {
                        saveData.Write(dataOffset, ItemData.EncodeItem((Item)houseDataType.GetField(field.Name).GetValue(Data)), saveData.IsBigEndian);
                    }
                    else
                    {
                        saveData.Write(dataOffset, ((Item)houseDataType.GetField(field.Name).GetValue(Data)).ItemId, saveData.IsBigEndian);
                    }
                }
            }

            foreach (var r in Data.Rooms)
                r.Write();
        }
    }
}