﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Newtonsoft.Json.Linq;
using System;
using UnityEditor;
using System.IO;

public enum LayerType
{
    IMAGE,
    CONV,
    MAXPOOL,
    FC,
    OUTPUT
}

public struct LayerData
{
    public string name;
    public LayerType type;
    public int[] weightShape;
    public List<Array> weightTensors;
    public int[] activationShape;
    public List<Array> activationTensors;
}

/// <summary>
/// Class for handling the loading of TensFlow ckpt data from a converted Json file.
/// </summary>
public class TFLoader : MonoBehaviour {

    public List<GameObject> prefabs;

    public Text classLabel;

    // Use this for initialization
    void Start () {

    }

    /// <summary>
    /// Main function of this class, calls necessary sub functions to load the Tensor Data.
    /// </summary>
    public void Load()
    {
        
        // clear scene from layers before loading TensorFlow model. Depends on Layer Tag (should be set on layer prefabs)
        foreach(GameObject go in GameObject.FindGameObjectsWithTag("Layer"))
        {
            GameObject.Destroy(go);
        }

        List<LayerData> layerList = ReadGraph();

        GameObject input = null;

        int count = 0;

        foreach (LayerData l in layerList)
        {
            bool isOutput = false;
            if (count == layerList.Count - 1)
            {
                isOutput = true;
            }
            input = InstantiateLayer(l, input, isOutput);


            count++;
        }
    }

    // Update is called once per frame
    void Update () {
		
	}

    /// <summary>
    /// Reads the Json file for a (for now) hardcoded number of epochs (0: initial weights + epochs 1-10).
    /// </summary>
    /// <param name="testSample"></param>
    /// <returns></returns>
    public List<LayerData> ReadGraph()
    {
        List<LayerData> layerDataList = new List<LayerData>();

        string path = EditorUtility.OpenFolderPanel("Load json folder", "", "");
        string[] files = Directory.GetFiles(path, "*.json");

        for (int e = 0; e < files.Length; e++)
        {
            JArray layerArray = JArray.Parse(System.IO.File.ReadAllText(files[e]));

            for (int i = 0; i < layerArray.Count-1; i++)
            {
                LayerData layerData;

                if (layerDataList.Count > i)
                {
                    layerData = layerDataList[i];
                } else
                {
                    layerData = new LayerData();
                    layerData.weightTensors = new List<Array>();
                    layerData.activationTensors = new List<Array>();
                    layerDataList.Add(layerData);
                }

                JArray layerContent = (JArray)layerArray[i];
                JToken className = layerContent.First;
                JToken name = className.Next;
                JArray weightShape = (JArray)name.Next;
                JArray weightTensor = (JArray)weightShape.Next;

                classLabel.text = (string)className;
                layerData.name = (string)name;

                int weightShapeRank = weightShape.Count;
                layerData.weightShape = new int[weightShapeRank];

                this.SetShapeAndTensorList(weightShape, weightTensor, layerData.weightShape, layerData.weightTensors);

                JArray activationShape = (JArray)weightTensor.Next;
                JArray activationTensor = (JArray)activationShape.Next;
                int activationShapeRank = activationShape.Count;
                layerData.activationShape = new int[activationShapeRank];

                this.SetShapeAndTensorList(activationShape, activationTensor, layerData.activationShape, layerData.activationTensors);

                layerData.type = LayerTypeFromName(layerData.name);

                layerDataList[i] = layerData;
            }

            JArray classNames = (JArray)layerArray[layerArray.Count-1];
            foreach(JToken name in classNames)
            {
                string className = (string)name;
                GlobalManager.Instance.classNames.Add(className);
            }
        }

        return layerDataList;
    }

    /// <summary>
    /// Handles the extraction of the tensor values out of the JArray objects and fills the target datacontainers accordingly.
    /// </summary>
    /// <param name="shape"></param>
    /// <param name="tensor"></param>
    /// <param name="targetShape"></param>
    /// <param name="targetTensorList"></param>
    private void SetShapeAndTensorList(JArray shape, JArray tensor, int[] targetShape, List<Array> targetTensorList)
    {
        int rank = shape.Count;
        int[] intShape = new int[shape.Count];
        int totalElements = 1;
        int[] perDimElements = new int[rank];
        perDimElements[0] = (int)shape[0];
        for (int j = 0; j < rank; j++)
        {
            intShape[j] = (int)shape[j];
            totalElements *= intShape[j];

            if (j > 0)
                perDimElements[j] = perDimElements[j - 1] * intShape[j];
        }

        for(int i=0; i<intShape.Length; i++)
        {
            targetShape[i] = intShape[i];
        }

        Array weightTensor = Array.CreateInstance(typeof(float), intShape);

        for (int t = 0; t < totalElements; t++)
        {
            JArray currentArray = tensor;
            int[] multiDimIndex = Util.GetMultiDimIndices(intShape, t);
            for (int j = 0; j < rank - 1; j++)
            {
                int currentLength = currentArray.Count;
                currentArray = (JArray)currentArray[multiDimIndex[j]];
            }
            float value = (float)currentArray[multiDimIndex[rank - 1]];
            weightTensor.SetValue(value, multiDimIndex);
        }

        targetTensorList.Add(weightTensor);
    }

    /// <summary>
    /// returns Layertype enum based on terms in the layer name.
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    LayerType LayerTypeFromName(string name)
    {
        if (name.Contains("conv"))
            return LayerType.CONV;
        else if (name.Contains("input"))
            return LayerType.IMAGE;
        else if (name.Contains("fc"))
            return LayerType.FC;
        else if (name.Contains("maxpool"))
            return LayerType.MAXPOOL;

        return LayerType.IMAGE;
    }


    /// <summary>
    /// Instantiates Layer from LayerData and sets initial values.
    /// </summary>
    /// <param name="l"></param>
    /// <param name="input"></param>
    /// <param name="isOutput"></param>
    /// <returns></returns>
    GameObject InstantiateLayer(LayerData l, GameObject input, bool isOutput)
    {
        switch (l.type)
        {
            case LayerType.IMAGE:
                {
                    GameObject go = GetImageLayerPrefab();
                    GameObject inst = Instantiate(go);
                    ImageLayer imLayer = inst.GetComponent<ImageLayer>();
                    imLayer.reducedResolution = new Vector2Int(l.weightShape[1], l.weightShape[2]);
                    imLayer.fullResolution = new Vector2Int(l.weightShape[1], l.weightShape[2]);
                    imLayer.pixelSpacing = 0.025f;
                    imLayer.depth = l.weightShape[3];

                    GameObject canvas = GameObject.FindGameObjectWithTag("Canvas");
                    Material pixelMaterial = canvas.GetComponent<GuiManager>().pixelMaterial;

                    imLayer.rgb = false;
                    MeshRenderer meshRenderer = inst.GetComponent<MeshRenderer>();
                    meshRenderer.sharedMaterials[0] = pixelMaterial;

                    imLayer.SetActivationTensorShape(l.activationShape);
                    for (int i = 0; i < l.activationTensors.Count; i++)
                    {
                        imLayer.SetActivationTensorForEpoch(l.activationTensors[i], i);
                    }

                    return inst;
                 }

            case LayerType.CONV:
                {
                    GameObject go = GetConvLayerPrefab();
                    GameObject inst = Instantiate(go);
                    ConvLayer convLayer = inst.GetComponent<ConvLayer>();
                    convLayer.reducedShape = new Vector2Int(l.weightShape[0], l.weightShape[1]);
                    convLayer.reducedDepth = l.weightShape[3];
                    convLayer.fullDepth = l.weightShape[3];
                    convLayer._input = input;
                    convLayer.filterSpread = 1.0f;
                    convLayer.lineCircleGrid = 2.0f;
                    convLayer.filterSpacing = 0.025f;

                    convLayer.SetWeightTensorShape(l.weightShape);
                    for(int i=0; i<l.weightTensors.Count; i++)
                    {
                        convLayer.SetWeightTensorForEpoch(l.weightTensors[i], i);
                    }

                    convLayer.SetActivationTensorShape(l.activationShape);
                    for (int i = 0; i < l.activationTensors.Count; i++)
                    {
                        convLayer.SetActivationTensorForEpoch(l.activationTensors[i], i);
                    }
                    return inst;
                }

            case LayerType.MAXPOOL:
                {
                    GameObject go = GetMaxPoolLayerPrefab();
                    GameObject inst = Instantiate(go);
                    MaxPoolLayer mpLayer = inst.GetComponent<MaxPoolLayer>();
                    mpLayer.filterSpacing = 0.025f;
                    mpLayer.zOffset = 0.25f;
                    mpLayer._input = input;

                    mpLayer.SetActivationTensorShape(l.activationShape);
                    for (int i = 0; i < l.activationTensors.Count; i++)
                    {
                        mpLayer.SetActivationTensorForEpoch(l.activationTensors[i], i);
                    }
                    return inst;
                }

            case LayerType.FC:
                {
                    GameObject go = GetFCLayerPrefab();
                    GameObject inst = Instantiate(go);
                    FCLayer fcLayer = inst.GetComponent<FCLayer>();
                    fcLayer._input = input;
                    fcLayer.filterSpacing = 0.025f;
                    fcLayer.reducedDepth = l.weightShape[1];
                    fcLayer.fullDepth = l.weightShape[1];
                    
                    //TODO: here loading is reducing non output fc layers automatically by 4
                    if (!l.name.Contains("out"))
                    {
                        fcLayer.reducedDepth = l.weightShape[1];

                    }else
                    {
                        fcLayer.lineCircleGrid = 0;
                    }
                    if (l.name.Contains("0"))
                    {
                        fcLayer.collapseInput = 1f;
                    }
                    //fcLayer.edgeBundle = 1.0f;
                    fcLayer.SetTensorShape(l.weightShape);
                    for (int i = 0; i < l.weightTensors.Count; i++)
                    {
                        fcLayer.SetTensorForEpoch(l.weightTensors[i], i);
                    }

                    fcLayer.SetActivationTensorShape(l.activationShape);
                    for (int i = 0; i < l.activationTensors.Count; i++)
                    {
                        fcLayer.SetActivationTensorForEpoch(l.activationTensors[i], i);

                        if (l.name.Contains("out")){;

                            Array tensor = l.activationTensors[i];
                            float maxFloat = 0;
                            int maxIndex = 0;

                            for(int j = 0; j<tensor.Length; j++)
                            {
                                int[] index = { 0, j };
                                float tensorVal = (float)tensor.GetValue(index);
                                if(tensorVal > maxFloat)
                                {
                                    maxFloat = tensorVal;
                                    maxIndex = j;
                                }
                            }

                            GlobalManager.Instance.predPerEpoch[i] = GlobalManager.Instance.classNames[maxIndex];
                        }


                    }
                    return inst;
                }

            default:
                break;
        }
        return null;
    }

    GameObject GetImageLayerPrefab()
    {
        return prefabs[0];
    }

    GameObject GetConvLayerPrefab()
    {
        return prefabs[1];
    }

    GameObject GetMaxPoolLayerPrefab()
    {
        return prefabs[2];
    }

    GameObject GetFCLayerPrefab()
    {
        return prefabs[3];
    }
}
