using Microsoft.Azure.WebJobs.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CallCenterSkills.Models
{

    public class Conversation
    {
        public string speaker { get; set; }
        public string text { get; set; }
        public long offset { get; set; }
        public long duration { get; set; }
        public float offset_in_seconds { get; set; }
        public float duration_in_seconds { get; set; }
        public float sentiment { get; set; }
        public string[] key_phrases { get; set; }
        public string[] people { get; set; }
        public object[] locations { get; set; }
        public string[] organizations { get; set; }
        public int turn { get; set; }
    }
    public class ConversationSummary
    {
        public int Turns { get; set; }
        public float LowestSentiment { get; set; }
        public float HighestSentiment { get; set; }
        public Tuple<int, float> MaxChange { get; set; }
        public KeyMoment Moment { get; set; }
        public float AverageSentiment { get; set; }
     

        internal static KeyMoment GetKeyMoment(List<Conversation> sortedList)
        {
            int currTurn = 0;
            int prevTurn = 0;
            int prevCustUtterance = 0;
            float prev = -1;
            for(int i =0; i < sortedList.Count; i++)
            {
                sortedList[i].turn = i;
                if (sortedList[i].speaker == "0")
                    continue;
                if(prev == -1)
                {
                    prev = sortedList[i].sentiment;
                    continue;
                }
                if (sortedList[i].sentiment < prev)
                {
                    if (currTurn > 0)
                    {
                        if (prev - sortedList[i].sentiment > sortedList[currTurn].sentiment)
                        {
                            currTurn = i;
                            prevCustUtterance = prevTurn;
                        }
                            

                        
                    }
                    else
                    {
                        currTurn = i;
                        prevCustUtterance = prevTurn;
                    }





                }
                prev = sortedList[i].sentiment;
                prevTurn = i;



            }

            KeyMoment moment = new KeyMoment();
            moment.Offset = sortedList[currTurn].offset_in_seconds;
            moment.SentimentDrop = sortedList[prevCustUtterance].sentiment - sortedList[currTurn].sentiment;
            moment.Turn = sortedList[currTurn].turn;
            return moment;

        }
    }
    public class KeyMoment
    {
        public int Turn { get; set; }
        public double SentimentDrop { get; set; }
        public double Offset { get; set; }
    }


}
