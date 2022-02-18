using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using TMPro;

//Note: don't forget to add the Ray Perception Sensor 3D component manually
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(MeshCollider))]
[RequireComponent(typeof(DecisionRequester))]
public class NNCar : Agent
{
    [System.Serializable]
    public class RewardInfo
    {
        public float mult_forward = 0.01f;
        public float mult_barrier = -1.0f;
        public float mult_car = -0.8f;
        public float wrongdirection = -0.5f;
        public float mult_position = 0.1f;
    }

    public string DriverName = "";
    public float Movespeed = 30;
    public float Turnspeed = 100;
    public RewardInfo rwd = new RewardInfo();
    public bool doEpisodes = true;
    [HideInInspector]
    public int checkpoints_passed = 0;
    public int finishLinePass = 0;

    private Rigidbody rb = null;
    private Vector3 recall_position;
    private Quaternion recall_rotation;
    private Bounds bnd;
    private bool isRightDirection;
    private string checkpoint_name = "";
    private Checkpoints script_checkpoints = null;
    private Leaderboard script_leaderboard = null;
    private TextMeshPro tmpro = null;

    public override void Initialize()
    {
        rb = this.GetComponent<Rigidbody>();
        rb.drag = 1;
        rb.angularDrag = 5;
        rb.interpolation = RigidbodyInterpolation.Extrapolate;

        this.GetComponent<MeshCollider>().convex = true;
        this.GetComponent<DecisionRequester>().DecisionPeriod = 1;
        bnd = this.GetComponent<MeshRenderer>().bounds;

        recall_position = new Vector3(this.transform.position.x, this.transform.position.y, this.transform.position.z);
        recall_rotation = new Quaternion(this.transform.rotation.x, this.transform.rotation.y, this.transform.rotation.z, this.transform.rotation.w);

        GameObject go = null;
        go = GameObject.Find("checkpoints");
        if (go != null)
            script_checkpoints = go.GetComponent<Checkpoints>();
        go = GameObject.Find("Leaderboard");
        if (go != null)
            script_leaderboard = go.GetComponent<Leaderboard>();

        DriverName = GenerateName();
        if (this.transform.Find("txtName") != null)
        {
            tmpro = this.transform.Find("txtName").GetComponent<TextMeshPro>();
            tmpro.text = DriverName;
        }
    }
    public override void OnEpisodeBegin()
    {
        rb.velocity = Vector3.zero;
        this.transform.position = recall_position;
        this.transform.rotation = recall_rotation;
    }
    public override void OnActionReceived(ActionBuffers actions)
    {
        //decisionrequestor component needed
        //  space type: discrete
        //      branches size: 2 move, turn
        //          branch 0 size: 3  fwd, nomove, back
        //          branch 1 size: 3  left, noturn, right

        if (isWheelsDown() == false)
            return;

        float mag = Mathf.Abs(rb.velocity.sqrMagnitude);

        switch (actions.DiscreteActions.Array[0])   //move
        {
            case 0:
                break;
            case 1:
                rb.AddRelativeForce(Vector3.back * Movespeed * Time.deltaTime, ForceMode.VelocityChange); //back
                break;
            case 2:
                rb.AddRelativeForce(Vector3.forward * Movespeed * Time.deltaTime, ForceMode.VelocityChange); //forward
                AddReward(mag * rwd.mult_forward);
                break;
        }

        switch (actions.DiscreteActions.Array[1])   //turn
        {
            case 0:
                break;
            case 1:
                this.transform.Rotate(Vector3.up, -Turnspeed * Time.deltaTime); //left
                break;
            case 2:
                this.transform.Rotate(Vector3.up, Turnspeed * Time.deltaTime); //right
                break;
        }        
    }
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        //Purpose:  for me to simulate the brain actions (I control the car with the keyboard)
        actionsOut.DiscreteActions.Array[0] = 0;
        actionsOut.DiscreteActions.Array[1] = 0;

        float move = Input.GetAxis("Vertical");     // -1..0..1  WASD arrowkeys
        float turn = Input.GetAxis("Horizontal");

        if (move < 0)
            actionsOut.DiscreteActions.Array[0] = 1;    //back
        else if (move > 0)
            actionsOut.DiscreteActions.Array[0] = 2;    //forward

        if (turn < 0)
            actionsOut.DiscreteActions.Array[1] = 1;    //left
        else if (turn > 0)
            actionsOut.DiscreteActions.Array[1] = 2;    //right
    }
    public override void CollectObservations(VectorSensor sensor)
    {
        //Note: behaviour parameter component > Vector observation size must be set to 1.
        //      Why? because here we are setting 1 manual observation
        
        bool isWhite = false;
        bool isYellow = false;
        int layermask = 1 << 6; //CarPerception layer

        RaycastHit[] hitWhite = Physics.RaycastAll(this.transform.position, this.transform.right, 20.0f, layermask);
        RaycastHit[] hitYellow = Physics.RaycastAll(this.transform.position, -this.transform.right, 20.0f, layermask);

        foreach(RaycastHit hit in hitWhite)
        {
            if (hit.collider.gameObject.CompareTag("BarrierWhite") == true)
            {
                isWhite = true;
                break;
            }
        }

        foreach(RaycastHit hit in hitYellow)
        {
            if (hit.collider.gameObject.CompareTag("BarrierYellow") == true)
            {
                isYellow = true;
                break;
            }
        }

        isRightDirection = (isWhite || isYellow);
        sensor.AddObservation(isRightDirection);

        if (isRightDirection == false)
            AddReward(rwd.wrongdirection);
    }
    private void OnCollisionEnter(Collision collision)
    {
        float mag = collision.relativeVelocity.sqrMagnitude;

        if (collision.gameObject.CompareTag("BarrierWhite") == true
            || collision.gameObject.CompareTag("BarrierYellow") == true)
        {
            AddReward(mag * rwd.mult_barrier);
            if (doEpisodes == true)
                EndEpisode();
        }
        else if (collision.gameObject.CompareTag("Car") == true)
        {
            AddReward(mag * rwd.mult_car);
            if (doEpisodes == true)
                EndEpisode();
        }
    }
    private void OnTriggerEnter(Collider other)
    {
        if (script_checkpoints != null)
        {
            if (other.CompareTag("Checkpoint") == true)
            {
                //have to first pass finish line to start racing
                if (other.name == "chk (0)")
                {
                    finishLinePass += 1;
                    checkpoint_name = other.name;
                }

                //didnt get to finish line yet no counting
                if (finishLinePass == 0)
                    return;

                //count checkpoints passed
                checkpoint_name = script_checkpoints.GetNextCheckpointName(checkpoint_name);
                checkpoints_passed += 1;
                if (script_leaderboard != null)
                {
                    int position = script_leaderboard.DoLeaderboard(DriverName);
                    if (position > -1)
                    {
                        tmpro.text = string.Format("{0} {1}", position, DriverName);
                        AddReward(rwd.mult_position / (float)position);
                    }
                    else
                    {
                        tmpro.text = DriverName;
                    }                        
                }
            }
        }        
    }
    private bool isWheelsDown()
    {
        //raycast down from car = ground should be closely there
        return Physics.Raycast(this.transform.position, -this.transform.up, bnd.size.y * 0.55f);
    }
    private  string GenerateName()
    {
        int len = Random.Range(3, 5);
        System.Random r = new System.Random();
        string[] consonants = { "b", "c", "d", "f", "g", "h", "j", "k", "l", "m", "l", "n", "p", "q", "r", "s", "sh", "zh", "t", "v", "w", "x" };
        string[] vowels = { "a", "e", "i", "o", "u", "ae", "y" };
        string Name = "";
        Name += consonants[r.Next(consonants.Length)].ToUpper();
        Name += vowels[r.Next(vowels.Length)];
        int b = 2; //b tells how many times a new letter has been added. It's 2 right now because the first two letters are already in the name.
        while (b < len)
        {
            Name += consonants[r.Next(consonants.Length)];
            b++;
            Name += vowels[r.Next(vowels.Length)];
            b++;
        }

        return Name;


    }
}
